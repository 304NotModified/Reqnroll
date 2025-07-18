using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Reqnroll.BoDi;
using Reqnroll.Analytics;
using Reqnroll.Bindings;
using Reqnroll.Bindings.Reflection;
using Reqnroll.Configuration;
using Reqnroll.ErrorHandling;
using Reqnroll.Events;
using Reqnroll.PlatformCompatibility;
using Reqnroll.Plugins;
using Reqnroll.Tracing;
using Reqnroll.UnitTestProvider;

namespace Reqnroll.Infrastructure
{
    public class TestExecutionEngine : ITestExecutionEngine
    {
        private readonly IAsyncBindingInvoker _bindingInvoker;
        private readonly IBindingRegistry _bindingRegistry;
        private readonly IContextManager _contextManager;
        private readonly IErrorProvider _errorProvider;
        private readonly IObsoleteStepHandler _obsoleteStepHandler;
        private readonly ReqnrollConfiguration _reqnrollConfiguration;
        private readonly IStepArgumentTypeConverter _stepArgumentTypeConverter;
        private readonly IStepDefinitionMatchService _stepDefinitionMatchService;
        private readonly IStepFormatter _stepFormatter;
        private readonly ITestObjectResolver _testObjectResolver;
        private readonly ITestRunContext _testRunContext;
        private readonly ITestTracer _testTracer;
        private readonly IUnitTestRuntimeProvider _unitTestRuntimeProvider;
        private readonly IAnalyticsEventProvider _analyticsEventProvider;
        private readonly IAnalyticsTransmitter _analyticsTransmitter;
        private readonly ITestRunnerManager _testRunnerManager;
        private readonly IRuntimePluginTestExecutionLifecycleEventEmitter _runtimePluginTestExecutionLifecycleEventEmitter;
        private readonly ITestThreadExecutionEventPublisher _testThreadExecutionEventPublisher;
        private readonly ITestPendingMessageFactory _testPendingMessageFactory;
        private readonly ITestUndefinedMessageFactory _testUndefinedMessageFactory;
        private readonly object _testRunnerEndExecutedLock = new object();

        private bool _testRunnerEndExecuted = false;
        private bool _testRunnerStartExecuted = false;

        public TestExecutionEngine(
            IStepFormatter stepFormatter,
            ITestTracer testTracer,
            IErrorProvider errorProvider,
            IStepArgumentTypeConverter stepArgumentTypeConverter,
            ReqnrollConfiguration reqnrollConfiguration,
            IBindingRegistry bindingRegistry,
            IUnitTestRuntimeProvider unitTestRuntimeProvider,
            IContextManager contextManager,
            IStepDefinitionMatchService stepDefinitionMatchService,
            IAsyncBindingInvoker bindingInvoker,
            IObsoleteStepHandler obsoleteStepHandler,
            IAnalyticsEventProvider analyticsEventProvider,
            IAnalyticsTransmitter analyticsTransmitter,
            ITestRunnerManager testRunnerManager,
            IRuntimePluginTestExecutionLifecycleEventEmitter runtimePluginTestExecutionLifecycleEventEmitter,
            ITestThreadExecutionEventPublisher testThreadExecutionEventPublisher,
            ITestPendingMessageFactory testPendingMessageFactory,
            ITestUndefinedMessageFactory testUndefinedMessageFactory,
            ITestObjectResolver testObjectResolver,
            ITestRunContext testRunContext)
        {
            _errorProvider = errorProvider;
            _bindingInvoker = bindingInvoker;
            _contextManager = contextManager;
            _unitTestRuntimeProvider = unitTestRuntimeProvider;
            _bindingRegistry = bindingRegistry;
            _reqnrollConfiguration = reqnrollConfiguration;
            _testTracer = testTracer;
            _stepFormatter = stepFormatter;
            _stepArgumentTypeConverter = stepArgumentTypeConverter;
            _stepDefinitionMatchService = stepDefinitionMatchService;
            _testObjectResolver = testObjectResolver;
            _testRunContext = testRunContext;
            _obsoleteStepHandler = obsoleteStepHandler;
            _analyticsEventProvider = analyticsEventProvider;
            _analyticsTransmitter = analyticsTransmitter;
            _testRunnerManager = testRunnerManager;
            _runtimePluginTestExecutionLifecycleEventEmitter = runtimePluginTestExecutionLifecycleEventEmitter;
            _testThreadExecutionEventPublisher = testThreadExecutionEventPublisher;
            _testPendingMessageFactory = testPendingMessageFactory;
            _testUndefinedMessageFactory = testUndefinedMessageFactory;
        }

        public FeatureContext FeatureContext => _contextManager.FeatureContext;

        public ScenarioContext ScenarioContext => _contextManager.ScenarioContext;
        public ITestThreadContext TestThreadContext => _contextManager.TestThreadContext;
        public ITestRunContext TestRunContext => _testRunContext;

        public virtual async Task OnTestRunStartAsync()
        {
            if (_testRunnerStartExecuted)
            {
                return;
            }

            if (_analyticsTransmitter.IsEnabled)
            {
                _ = Task.Run(TryTransmitReqnrollProjectRunningEventAsync);
            }

            _testRunnerStartExecuted = true;

            await _testThreadExecutionEventPublisher.PublishEventAsync(new TestRunStartedEvent());

            // The 'FireEventsAsync' call might throw an exception if the related before test run hook fails,
            // but we can let the exception propagate to the caller.
            await FireEventsAsync(HookType.BeforeTestRun);
        }

        async Task TryTransmitReqnrollProjectRunningEventAsync()
        {
            try
            {
                var testAssemblyName = _testRunnerManager.TestAssembly.GetName().Name;
                var projectRunningEvent = _analyticsEventProvider.CreateProjectRunningEvent(testAssemblyName);
                await _analyticsTransmitter.TransmitReqnrollProjectRunningEventAsync(projectRunningEvent);
            }
            catch (Exception ex)
            {
                // catch all exceptions since we do not want to break anything
                Debug.WriteLine(ex, "Sending telemetry failed");
            }
        }

        public virtual async Task OnTestRunEndAsync()
        {
            lock (_testRunnerEndExecutedLock)
            {
                if (_testRunnerEndExecuted)
                {
                    return;
                }

                _testRunnerEndExecuted = true;
            }

            try
            {
                await FireEventsAsync(HookType.AfterTestRun);
            }
            finally
            {
                await _testThreadExecutionEventPublisher.PublishEventAsync(new TestRunFinishedEvent());
            }
        }

        public virtual async Task OnFeatureStartAsync(FeatureInfo featureInfo)
        {
            _contextManager.InitializeFeatureContext(featureInfo);

            await _testThreadExecutionEventPublisher.PublishEventAsync(new FeatureStartedEvent(FeatureContext));

            // The 'FireEventsAsync' call might throw an exception if the related before feature hook fails,
            // but we can let the exception propagate to the caller.
            try
            {
                await FireEventsAsync(HookType.BeforeFeature);
            }
            catch (Exception e)
            {
                // we capture the exception here to be able to skip subsequent scenario execution in the same feature
                _contextManager.FeatureContext.BeforeFeatureHookError = e;
                throw;
            }
        }

        public virtual async Task OnFeatureEndAsync()
        {
            try
            {
                await FireEventsAsync(HookType.AfterFeature);
            }
            finally
            {
                if (_reqnrollConfiguration.TraceTimings)
                {
                    FeatureContext.Stopwatch.Stop();
                    var duration = FeatureContext.Stopwatch.Elapsed;
                    _testTracer.TraceDuration(duration, "Feature: " + FeatureContext.FeatureInfo.Title);
                }

                await _testThreadExecutionEventPublisher.PublishEventAsync(new FeatureFinishedEvent(FeatureContext));

                _contextManager.CleanupFeatureContext();
            }
        }

        public virtual void OnScenarioInitialize(ScenarioInfo scenarioInfo, RuleInfo ruleInfo)
        {
            _contextManager.InitializeScenarioContext(scenarioInfo, ruleInfo);
        }

        public virtual async Task OnScenarioStartAsync()
        {
            await _testThreadExecutionEventPublisher.PublishEventAsync(new ScenarioStartedEvent(FeatureContext, ScenarioContext));

            try
            {
                await FireScenarioEventsAsync(HookType.BeforeScenario);
            }
            catch (Exception ex)
            {
                if (_contextManager.ScenarioContext != null)
                {
                    _contextManager.ScenarioContext.ScenarioExecutionStatus = ScenarioExecutionStatus.TestError;
                    _contextManager.ScenarioContext.TestError = ex;
                }
            }
        }

        public virtual async Task OnAfterLastStepAsync()
        {
            await HandleBlockSwitchAsync(ScenarioBlock.None);

            if (_reqnrollConfiguration.TraceTimings)
            {
                _contextManager.ScenarioContext.Stopwatch.Stop();
                var duration = _contextManager.ScenarioContext.Stopwatch.Elapsed;
                _testTracer.TraceDuration(duration, "Scenario: " + _contextManager.ScenarioContext.ScenarioInfo.Title);
            }

            switch (_contextManager.ScenarioContext.ScenarioExecutionStatus)
            {
                case ScenarioExecutionStatus.OK: return;
                case ScenarioExecutionStatus.Skipped:
                    _unitTestRuntimeProvider.TestIgnore("Scenario ignored using @Ignore tag");
                    return;
                case ScenarioExecutionStatus.StepDefinitionPending:
                    string pendingStepExceptionMessage = _testPendingMessageFactory.BuildFromScenarioContext(_contextManager.ScenarioContext);
                    _errorProvider.ThrowPendingError(_contextManager.ScenarioContext.ScenarioExecutionStatus, pendingStepExceptionMessage);
                    return;
                case ScenarioExecutionStatus.UndefinedStep:
                    string undefinedStepExceptionMessage = _testUndefinedMessageFactory.BuildFromContext(_contextManager.ScenarioContext, _contextManager.FeatureContext);
                    _errorProvider.ThrowPendingError(_contextManager.ScenarioContext.ScenarioExecutionStatus, undefinedStepExceptionMessage);
                    return;
            }

            if (_contextManager.ScenarioContext.TestError == null)
            {
                throw new InvalidOperationException("test failed with an unknown error");
            }

            _contextManager.ScenarioContext.TestError.PreserveStackTrace();
            throw _contextManager.ScenarioContext.TestError;
        }

        public virtual async Task OnScenarioEndAsync()
        {
            // We invoke the before/after feature hooks from test setup, but we initialize the scenario context in the test method.
            // In case the feature hooks fail, the runner will not run the test method, but the test teardown method will be called.
            // In this case we don't have an initialized scenario context, so we don't need to call the after scenario hooks.
            if (_contextManager.ScenarioContext == null)
                return;

            try
            {
                if (_contextManager.ScenarioContext.ScenarioExecutionStatus != ScenarioExecutionStatus.Skipped)
                {
                    await FireScenarioEventsAsync(HookType.AfterScenario);
                }
            }
            finally
            {
                await _testThreadExecutionEventPublisher.PublishEventAsync(new ScenarioFinishedEvent(FeatureContext, ScenarioContext));

                _contextManager.CleanupScenarioContext();
            }
        }

        public virtual async Task OnScenarioSkippedAsync()
        {
            // after discussing the placement of message sending points, this placement causes far less effort than rewriting the whole logic
            _contextManager.ScenarioContext.ScenarioExecutionStatus = ScenarioExecutionStatus.Skipped;

            // in case of skipping a Scenario, the OnScenarioStart() is not called, so publish the event here
            await _testThreadExecutionEventPublisher.PublishEventAsync(new ScenarioStartedEvent(FeatureContext, ScenarioContext));
            await _testThreadExecutionEventPublisher.PublishEventAsync(new ScenarioSkippedEvent());
        }

        public virtual void Pending()
        {
            throw _errorProvider.GetPendingStepDefinitionError();
        }

        protected virtual async Task OnBlockStartAsync(ScenarioBlock block)
        {
            if (block == ScenarioBlock.None)
                return;

            await FireScenarioEventsAsync(HookType.BeforeScenarioBlock);
        }

        protected virtual async Task OnBlockEndAsync(ScenarioBlock block)
        {
            if (block == ScenarioBlock.None)
                return;

            await FireScenarioEventsAsync(HookType.AfterScenarioBlock);
        }

        protected virtual async Task OnStepStartAsync()
        {
            await FireScenarioEventsAsync(HookType.BeforeStep);
        }

        protected virtual async Task OnStepEndAsync()
        {
            await FireScenarioEventsAsync(HookType.AfterStep);
        }
        
        protected virtual async Task OnSkipStepAsync()
        {
            _contextManager.StepContext.Status = ScenarioExecutionStatus.Skipped;
            _testTracer.TraceStepSkipped();
            await _testThreadExecutionEventPublisher.PublishEventAsync(new StepSkippedEvent());


            var skippedStepHandlers = _contextManager.ScenarioContext.ScenarioContainer.ResolveAll<ISkippedStepHandler>().ToArray();
            foreach (var skippedStepHandler in skippedStepHandlers)
            {
                skippedStepHandler.Handle(_contextManager.ScenarioContext);
            }
        }

        #region Step/event execution

        protected virtual async Task FireScenarioEventsAsync(HookType bindingEvent)
        {
            await FireEventsAsync(bindingEvent);
        }

        private async Task FireEventsAsync(HookType hookType)
        {
            await _testThreadExecutionEventPublisher.PublishEventAsync(new HookStartedEvent(hookType, FeatureContext, ScenarioContext, _contextManager.StepContext));
            var stepContext = _contextManager.GetStepContext();

            var matchingHooks = _bindingRegistry.GetHooks(hookType)
                .Where(hookBinding => !hookBinding.IsScoped ||
                                      hookBinding.BindingScope.Match(stepContext, out int _));

            //HACK: The InvokeHook requires an IHookBinding that contains the scope as well
            // if multiple scopes match the same method, we take the first one.
            // The InvokeHook uses only the Method anyway...
            // The only problem could be if the same method is decorated with hook attributes using different order,
            // but in this case it is anyway impossible to tell the right ordering.
            var uniqueMatchingHooks = matchingHooks.GroupBy(hookBinding => hookBinding.Method).Select(g => g.First());
            Exception hookException = null;
            try
            {
                //Note: if a (user-)hook throws an exception the subsequent hooks of the same type are not executed
                foreach (var hookBinding in uniqueMatchingHooks.OrderBy(x => x.HookOrder))
                {
                    await InvokeHookAsync(_bindingInvoker, hookBinding, hookType);
                }
            }
            catch (Exception hookExceptionCaught)
            {
                hookException = hookExceptionCaught;
                SetHookError(hookType, hookException);
            }

            //Note: plugin-hooks are still executed even if a user-hook failed with an exception
            //A plugin-hook should not throw an exception under normal circumstances, exceptions are not handled/caught here
            await FireRuntimePluginTestExecutionLifecycleEventsAsync(hookType);

            await _testThreadExecutionEventPublisher.PublishEventAsync(new HookFinishedEvent(hookType, FeatureContext, ScenarioContext, _contextManager.StepContext, hookException));

            //Note: the (user-)hook exception (if any) will be thrown after the plugin hooks executed to fail the test with the right error
            if (hookException != null) ExceptionDispatchInfo.Capture(hookException).Throw();
        }

        private async Task FireRuntimePluginTestExecutionLifecycleEventsAsync(HookType hookType)
        {
            //We pass a container corresponding the type of event
            var container = GetHookContainer(hookType);
            await _runtimePluginTestExecutionLifecycleEventEmitter.RaiseExecutionLifecycleEventAsync(hookType, container);
        }

        public virtual async Task InvokeHookAsync(IAsyncBindingInvoker invoker, IHookBinding hookBinding, HookType hookType)
        {
            var currentContainer = GetHookContainer(hookType);
            var arguments = ResolveArguments(hookBinding, currentContainer);

            await _testThreadExecutionEventPublisher.PublishEventAsync(new HookBindingStartedEvent(hookBinding, _contextManager));
            var durationHolder = new DurationHolder();
            Exception exceptionthrown = null;
            try
            {
                await invoker.InvokeBindingAsync(hookBinding, _contextManager, arguments, _testTracer, durationHolder);
            }
            catch (Exception exception)
            {
                // This exception is caught in order to be able to inform consumers of the HookBindingFinishedEvent;
                // The throw; statement ensures that the exception is propagated up to the FireEventsAsync method
                exceptionthrown = exception;
                throw;
            }
            finally
            {
                await _testThreadExecutionEventPublisher.PublishEventAsync(new HookBindingFinishedEvent(hookBinding, durationHolder.Duration, _contextManager, exceptionthrown));
            }
        }

        private IObjectContainer GetHookContainer(HookType hookType)
        {
            IObjectContainer currentContainer;
            switch (hookType)
            {
                case HookType.BeforeTestRun:
                case HookType.AfterTestRun:
                    currentContainer = _testRunContext.TestRunContainer;
                    break;
                case HookType.BeforeFeature:
                case HookType.AfterFeature:
                    currentContainer = FeatureContext.FeatureContainer;
                    break;
                default: // scenario scoped hooks
                    currentContainer = ScenarioContext.ScenarioContainer;
                    break;
            }

            return currentContainer;
        }

        private IReqnrollContext GetHookContext(HookType hookType)
        {
            switch (hookType)
            {
                case HookType.BeforeTestRun:
                case HookType.AfterTestRun:
                    return TestRunContext;
                case HookType.BeforeFeature:
                case HookType.AfterFeature:
                    return _contextManager.FeatureContext;
                default: // scenario scoped hooks
                    return _contextManager.ScenarioContext;
            }
        }

        private void SetHookError(HookType hookType, Exception hookException)
        {
            var context = GetHookContext(hookType);
            if (context is { TestError: null } and ReqnrollContext reqnrollContext)
                reqnrollContext.TestError = hookException;

            if (context is ScenarioContext scenarioContext)
            {
                scenarioContext.ScenarioExecutionStatus = ScenarioExecutionStatus.TestError;
            }
        }

        private object[] ResolveArguments(IHookBinding hookBinding, IObjectContainer currentContainer)
        {
            if (hookBinding.Method == null || !hookBinding.Method.Parameters.Any())
                return null;
            return hookBinding.Method.Parameters.Select(p => ResolveArgument(currentContainer, p)).ToArray();
        }

        private object ResolveArgument(IObjectContainer container, IBindingParameter parameter)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            if (parameter == null) throw new ArgumentNullException(nameof(parameter));

            if (parameter.Type is RuntimeBindingType runtimeParameterType)
            {
                return _testObjectResolver.ResolveBindingInstance(runtimeParameterType.Type, container);
            }

            throw new ReqnrollException("Parameters can only be resolved for runtime methods.");

        }

        private async Task ExecuteStepAsync(IContextManager contextManager, StepInstance stepInstance)
        {
            // The 'HandleBlockSwitchAsync' call might throw an exception if any related before/after block hook fails.
            // This exception will be propagated to the caller, so we don't need to handle it here.
            await HandleBlockSwitchAsync(stepInstance.StepDefinitionType.ToScenarioBlock());

            _testTracer.TraceStep(stepInstance, true);

            bool isStepSkipped = contextManager.ScenarioContext.ScenarioExecutionStatus != ScenarioExecutionStatus.OK;
            bool onStepStartExecuted = false;

            BindingMatch match = null;
            object[] arguments = null;
            var durationHolder = new DurationHolder();
            try
            {
                match = GetStepMatch(stepInstance);
                contextManager.StepContext.StepInfo.BindingMatch = match;
                contextManager.StepContext.StepInfo.StepInstance = stepInstance;

                if (isStepSkipped)
                {
                    await OnSkipStepAsync();
                }
                else
                {
                    arguments = await GetExecuteArgumentsAsync(match);
                    _obsoleteStepHandler.Handle(match);

                    onStepStartExecuted = true;
                    // Both 'OnStepStartAsync' and 'ExecuteStepMatchAsync' can throw exceptions
                    // if the related hook or step definition fails.
                    await OnStepStartAsync();
                    await ExecuteStepMatchAsync(match, arguments, durationHolder);
                    if (_reqnrollConfiguration.TraceSuccessfulSteps)
                        _testTracer.TraceStepDone(match, arguments, durationHolder.Duration);
                }
            }
            catch (PendingStepException)
            {
                Debug.Assert(match != null);
                Debug.Assert(arguments != null);

                _testTracer.TraceStepPending(match, arguments);
                contextManager.ScenarioContext.PendingSteps.Add(
                    _stepFormatter.GetMatchText(match, arguments));

                UpdateStatusOnStepFailure(ScenarioExecutionStatus.StepDefinitionPending, null);
            }
            catch (MissingStepDefinitionException)
            {
                UpdateStatusOnStepFailure(ScenarioExecutionStatus.UndefinedStep, null);
            }
            catch (BindingException ex)
            {
                _testTracer.TraceBindingError(ex);
                UpdateStatusOnStepFailure(ScenarioExecutionStatus.BindingError, ex);

            }
            catch (Exception ex)
            {
                _testTracer.TraceError(ex, durationHolder.Duration);

                UpdateStatusOnStepFailure(ScenarioExecutionStatus.TestError, ex);

                if (_reqnrollConfiguration.StopAtFirstError)
                    throw;
            }
            finally
            {
                if (onStepStartExecuted)
                {
                    // We need to have this call after the 'UpdateStatusOnStepFailure' above, because otherwise
                    // after step hooks cannot handle step errors.
                    // The 'OnStepEndAsync' call might throw an exception if the related after step hook fails,
                    // but we can let the exception propagate to the caller.
                    await OnStepEndAsync();
                }
            }
        }

        private void UpdateStatusOnStepFailure(ScenarioExecutionStatus stepStatus, Exception exception)
        {
            _contextManager.StepContext.Status = stepStatus;

            if (_contextManager.ScenarioContext.ScenarioExecutionStatus < stepStatus)
            {
                _contextManager.ScenarioContext.ScenarioExecutionStatus = stepStatus;

                if (exception != null)
                {
                    _contextManager.ScenarioContext.TestError = exception;
                }
            }
        }

        protected virtual BindingMatch GetStepMatch(StepInstance stepInstance)
        {
            if (!_bindingRegistry.IsValid)
                throw _errorProvider.GetInvalidBindingRegistryError(_bindingRegistry.GetErrorMessages());

            var match = _stepDefinitionMatchService.GetBestMatch(stepInstance, FeatureContext.BindingCulture, out var ambiguityReason, out var candidatingMatches);

            if (match.Success)
                return match;

            if (candidatingMatches.Any())
            {
                if (ambiguityReason == StepDefinitionAmbiguityReason.AmbiguousSteps)
                    throw _errorProvider.GetAmbiguousMatchError(candidatingMatches, stepInstance);

                if (ambiguityReason == StepDefinitionAmbiguityReason.ParameterErrors) // ambiguity, because of param error
                    throw _errorProvider.GetAmbiguousBecauseParamCheckMatchError(candidatingMatches, stepInstance);
            }

            _testTracer.TraceNoMatchingStepDefinition(stepInstance, FeatureContext.FeatureInfo.GenerationTargetLanguage, FeatureContext.BindingCulture, candidatingMatches);
            _contextManager.ScenarioContext.MissingSteps.Add(stepInstance);
            throw _errorProvider.GetMissingStepDefinitionError();
        }

        protected virtual async Task ExecuteStepMatchAsync(BindingMatch match, object[] arguments, DurationHolder durationHolder)
        {
            await _testThreadExecutionEventPublisher.PublishEventAsync(new StepBindingStartedEvent(match.StepBinding));
            
            try
            {
                await _bindingInvoker.InvokeBindingAsync(match.StepBinding, _contextManager, arguments, _testTracer, durationHolder);
            }
            finally
            {
                await _testThreadExecutionEventPublisher.PublishEventAsync(new StepBindingFinishedEvent(match.StepBinding, durationHolder.Duration));
            }
        }

        private async Task HandleBlockSwitchAsync(ScenarioBlock block)
        {
            if (_contextManager == null)
            {
                throw new ArgumentNullException(nameof(_contextManager));
            }
            
            if (_contextManager.ScenarioContext == null)
            {
                throw new ArgumentNullException(nameof(_contextManager.ScenarioContext));
            }

            if (_contextManager.ScenarioContext.CurrentScenarioBlock != block)
            {
                if (_contextManager.ScenarioContext.ScenarioExecutionStatus == ScenarioExecutionStatus.OK)
                {
                    // The 'OnBlockEndAsync' call might throw and exception if the related hook fails,
                    // but we can let the exception propagate to the caller, because in that case we don't 
                    // want to execute the next block anyway.
                    await OnBlockEndAsync(_contextManager.ScenarioContext.CurrentScenarioBlock);
                }

                _contextManager.ScenarioContext.CurrentScenarioBlock = block;

                if (_contextManager.ScenarioContext.ScenarioExecutionStatus == ScenarioExecutionStatus.OK)
                {
                    // The 'OnBlockStartAsync' call might throw and exception if the related hook fails,
                    // but we can let the exception propagate to the caller.
                    await OnBlockStartAsync(_contextManager.ScenarioContext.CurrentScenarioBlock);
                }
            }
        }

        private async Task<object[]> GetExecuteArgumentsAsync(BindingMatch match)
        {
            var bindingParameters = match.StepBinding.Method.Parameters.ToArray();
            if (match.Arguments.Length != bindingParameters.Length)
                throw _errorProvider.GetParameterCountError(match, match.Arguments.Length);

            var arguments = new object[match.Arguments.Length];

            for (var i = 0; i < match.Arguments.Length; i++)
            {
                arguments[i] = await ConvertArg(match.Arguments[i].Value, bindingParameters[i].Type);
            }

            return arguments;
        }

        private async Task<object> ConvertArg(object value, IBindingType typeToConvertTo)
        {
            Debug.Assert(value != null);
            Debug.Assert(typeToConvertTo != null);

            return await _stepArgumentTypeConverter.ConvertAsync(value, typeToConvertTo, FeatureContext.BindingCulture);
        }

        #endregion

        #region Given-When-Then

        public virtual async Task StepAsync(StepDefinitionKeyword stepDefinitionKeyword, string keyword, string text, string multilineTextArg, Table tableArg)
        {
            StepDefinitionType stepDefinitionType = stepDefinitionKeyword == StepDefinitionKeyword.And || stepDefinitionKeyword == StepDefinitionKeyword.But
                ? GetCurrentBindingType()
                : (StepDefinitionType) stepDefinitionKeyword;
            _contextManager.InitializeStepContext(new StepInfo(stepDefinitionType, text, tableArg, multilineTextArg));

            await _testThreadExecutionEventPublisher.PublishEventAsync(new StepStartedEvent(FeatureContext, ScenarioContext, _contextManager.StepContext));

            try
            {
                var stepInstance = new StepInstance(stepDefinitionType, stepDefinitionKeyword, keyword, text, multilineTextArg, tableArg, _contextManager.GetStepContext());
                await ExecuteStepAsync(_contextManager, stepInstance);
            }
            finally
            {
                await _testThreadExecutionEventPublisher.PublishEventAsync(new StepFinishedEvent(FeatureContext, ScenarioContext, _contextManager.StepContext));
                _contextManager.CleanupStepContext();
            }
        }

        private StepDefinitionType GetCurrentBindingType()
        {
            return _contextManager.CurrentTopLevelStepDefinitionType ?? StepDefinitionType.Given;
        }

        #endregion
    }
}
