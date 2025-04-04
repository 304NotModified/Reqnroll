using System;
using System.Threading.Tasks;
using Reqnroll.Analytics;
using Reqnroll.CommonModels;

namespace Reqnroll.Tools.MsBuild.Generation
{
    public class MSBuildTaskAnalyticsTransmitter : IMSBuildTaskAnalyticsTransmitter
    {
        private readonly IAnalyticsEventProvider _analyticsEventProvider;
        private readonly IMSBuildInformationProvider _msBuildInformationProvider;
        private readonly ReqnrollProjectInfo _reqnrollProjectInfo;
        private readonly IAnalyticsTransmitter _analyticsTransmitter;
        private readonly ITaskLoggingWrapper _taskLoggingWrapper;

        public MSBuildTaskAnalyticsTransmitter(
            IAnalyticsEventProvider analyticsEventProvider,
            IMSBuildInformationProvider msBuildInformationProvider,
            ReqnrollProjectInfo reqnrollProjectInfo,
            IAnalyticsTransmitter analyticsTransmitter,
            ITaskLoggingWrapper taskLoggingWrapper)
        {
            _analyticsEventProvider = analyticsEventProvider;
            _msBuildInformationProvider = msBuildInformationProvider;
            _reqnrollProjectInfo = reqnrollProjectInfo;
            _analyticsTransmitter = analyticsTransmitter;
            _taskLoggingWrapper = taskLoggingWrapper;
        }

        public async Task TryTransmitProjectCompilingEventAsync()
        {
            try
            {
                var transmissionResult = await TransmitProjectCompilingEventAsync();

                if (transmissionResult is IFailure failure)
                {
                    _taskLoggingWrapper.LogDiagnosticMessage($"Could not transmit analytics: {failure}");
                }
            }
            catch (Exception exc)
            {
                // catch all exceptions since we do not want to break the build simply because event creation failed
                // but still return an error containing the exception to at least log it
                _taskLoggingWrapper.LogDiagnosticMessage($"Could not transmit analytics: {exc}");
            }
        }

        public async Task<IResult> TransmitProjectCompilingEventAsync()
        {
            var projectCompilingEvent = _analyticsEventProvider.CreateProjectCompilingEvent(
                _msBuildInformationProvider.GetMSBuildVersion(),
                _reqnrollProjectInfo.ProjectAssemblyName,
                _reqnrollProjectInfo.TargetFrameworks,
                _reqnrollProjectInfo.CurrentTargetFramework,
                _reqnrollProjectInfo.ProjectGuid);
            return await _analyticsTransmitter.TransmitReqnrollProjectCompilingEventAsync(projectCompilingEvent);
        }
    }
}
