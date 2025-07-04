using System;
using System.Linq;
using System.Reflection;
using Moq;
using Xunit;
using Reqnroll.Bindings;
using Reqnroll.Bindings.Discovery;
using Reqnroll.Configuration;
using Reqnroll.Infrastructure;

namespace Reqnroll.RuntimeTests
{
    
    public class RuntimeBindingRegistryBuilderTests
    {
        public RuntimeBindingRegistryBuilderTests()
        {
            bindingSourceProcessorStub = new BindingSourceProcessorStub();
        }

        [Binding]
        public class StepTransformationExample
        {
            [StepArgumentTransformation("BindingRegistryTests")]
            public int Transform(string val)
            {
                return 42;
            }
            
            [StepArgumentTransformation(Regex="BindingRegistryTests2", Order = 5)]
            public int TransformWithRegexAndOrder(string val)
            {
                return 43;
            }
            
            [StepArgumentTransformation(Order = 10)]
            public int TransformWithOrderAndWithoutRegex(string val)
            {
                return 44;
            } 
        }

        private BindingSourceProcessorStub bindingSourceProcessorStub;

        /*        Steps that are feature scoped               */

        [Binding]
        public class ScopedStepTransformationExample
        {
            [Then("SpecificBindingRegistryTests")]
            [Scope(Feature = "SomeFeature")]
            public int Transform(string val)
            {
                return 42;
            }
        }

        [Binding]
        public class ScopedStepTransformationExampleTheOther
        {
            [Then("SpecificBindingRegistryTests")]
            [Scope(Feature = "AnotherFeature")]
            public int Transform(string val)
            {
                return 24;
            }
        }

        [Binding]
        public class ScopedHookExample
        {
            [BeforeScenario]
            [Scope(Tag = "tag1")]
            public void Tag1BeforeScenario()
            {
            }

            [BeforeScenario("tag2")]
            public void Tag2BeforeScenario()
            {
            }

            [BeforeScenario("tag3", "tag4")]
            public void Tag34BeforeScenario()
            {
            }
        }

        [Binding]
        public class PrioritizedHookExample
        {
            [BeforeScenario]
            public void OrderTenThousand()
            {
            }

            [Before(Order = 9000)]
            public void OrderNineThousand()
            {
            }

            [BeforeScenarioBlock(Order = 10001)]
            public void OrderTenThousandAnd1()
            {
            }

            [BeforeFeature(Order = 10002)]
            public static void OrderTenThousandAnd2()
            {
            }

            [BeforeStep(Order = 10003)]
            public void OrderTenThousandAnd3()
            {
            }

            [BeforeTestRun(Order = 10004)]
            public static void OrderTenThousandAnd4()
            {
            }

            [AfterScenario]
            public void AfterOrderTenThousand()
            {
            }

            [After(Order = 9000)]
            public void AfterOrderNineThousand()
            {
            }

            [AfterScenarioBlock(Order = 10001)]
            public void AfterOrderTenThousandAnd1()
            {
            }

            [AfterFeature(Order = 10002)]
            public static void AfterOrderTenThousandAnd2()
            {
            }

            [AfterStep(Order = 10003)]
            public void AfterOrderTenThousandAnd3()
            {
            }

            [AfterTestRun(Order = 10004)]
            public static void AfterOrderTenThousandAnd4()
            {
            }
        }

        [Binding]
        public class BindingClassWithStepDefinitionAttributes
        {
            [Given("I have done something")]
            public void GivenIHaveDoneSomething()
            {
            }

            [When("I do something")]
            public void WhenIDoSomething()
            {
            }

            [Then("something should happen")]
            public void ThenSomethingShouldHappen()
            {
            }
        }

        [Binding]
        public class BindingClassWithTranslatedStepDefinitionAttributes
        {
            public class AngenommenAttribute : GivenAttribute
            {
                public AngenommenAttribute(string expression) : base(expression, "de-DE")
                {
                }
            }
            public class WennAttribute : WhenAttribute
            {
                public WennAttribute(string expression) : base(expression, "de-DE")
                {
                }
            }
            public class DannAttribute : ThenAttribute
            {
                public DannAttribute(string expression) : base(expression, "de-DE")
                {
                }
            }

            [Angenommen("mache ich was")]
            public void AngenommenMacheIchWas()
            {
            }

            [Wenn("ich etwas mache")]
            public void WennIchEtwasMache()
            {
            }

            [Dann("sollte etwas passieren")]
            public void DannSollteEtwasPassieren()
            {
            }
        }

        [Binding]
        public class BindingClassWithCustomStepDefinitionAttribute
        {
            public class GivenAndWhenAttribute : StepDefinitionBaseAttribute
            {
                public GivenAndWhenAttribute(string expression)
                    : base(expression, new[] { StepDefinitionType.Given, StepDefinitionType.When } )
                {
                }
            }

            [GivenAndWhen("given and when")]
            public void GivenAndWhen()
            {
            }
        }

        private RuntimeBindingRegistryBuilder CreateSut()
        {
            return new RuntimeBindingRegistryBuilder(bindingSourceProcessorStub, new ReqnrollAttributesFilter(), new Mock<IBindingAssemblyLoader>().Object, ConfigurationLoader.GetDefault());
        }

        [Fact]
        public void ShouldFindBinding_WithDefaultOrder()
        {
            var builder = CreateSut();

            BuildCompleteBindingFromType(builder, typeof (ScopedHookExample));

            Assert.Equal(4, bindingSourceProcessorStub.HookBindings.Count(s => s.HookOrder == 10000));
        }

        [Fact]
        public void ShouldFindBinding_WithSpecifiedPriorities()
        {
            var builder = CreateSut();

            BuildCompleteBindingFromType(builder, typeof (PrioritizedHookExample));

            Assert.Equal(1,
                bindingSourceProcessorStub.HookBindings.Count(
                    s =>
                        s.HookType == HookType.BeforeScenario && s.Method.Name == "OrderTenThousand" &&
                        s.HookOrder == 10000));
            Assert.Equal(1,
                bindingSourceProcessorStub.HookBindings.Count(
                    s =>
                        s.HookType == HookType.BeforeScenario && s.Method.Name == "OrderNineThousand" &&
                        s.HookOrder == 9000));
            Assert.Equal(1,
                bindingSourceProcessorStub.HookBindings.Count(
                    s =>
                        s.HookType == HookType.BeforeScenarioBlock && s.Method.Name == "OrderTenThousandAnd1" &&
                        s.HookOrder == 10001));
            Assert.Equal(1,
                bindingSourceProcessorStub.HookBindings.Count(
                    s =>
                        s.HookType == HookType.BeforeFeature && s.Method.Name == "OrderTenThousandAnd2" &&
                        s.HookOrder == 10002));
            Assert.Equal(1,
                bindingSourceProcessorStub.HookBindings.Count(
                    s =>
                        s.HookType == HookType.BeforeStep && s.Method.Name == "OrderTenThousandAnd3" &&
                        s.HookOrder == 10003));
            Assert.Equal(1,
                bindingSourceProcessorStub.HookBindings.Count(
                    s =>
                        s.HookType == HookType.BeforeTestRun && s.Method.Name == "OrderTenThousandAnd4" &&
                        s.HookOrder == 10004));

            Assert.Equal(1,
                bindingSourceProcessorStub.HookBindings.Count(
                    s =>
                        s.HookType == HookType.AfterScenario && s.Method.Name == "AfterOrderTenThousand" &&
                        s.HookOrder == 10000));
            Assert.Equal(1,
                bindingSourceProcessorStub.HookBindings.Count(
                    s =>
                        s.HookType == HookType.AfterScenario && s.Method.Name == "AfterOrderNineThousand" &&
                        s.HookOrder == 9000));
            Assert.Equal(1,
                bindingSourceProcessorStub.HookBindings.Count(
                    s =>
                        s.HookType == HookType.AfterScenarioBlock && s.Method.Name == "AfterOrderTenThousandAnd1" &&
                        s.HookOrder == 10001));
            Assert.Equal(1,
                bindingSourceProcessorStub.HookBindings.Count(
                    s =>
                        s.HookType == HookType.AfterFeature && s.Method.Name == "AfterOrderTenThousandAnd2" &&
                        s.HookOrder == 10002));
            Assert.Equal(1,
                bindingSourceProcessorStub.HookBindings.Count(
                    s =>
                        s.HookType == HookType.AfterStep && s.Method.Name == "AfterOrderTenThousandAnd3" &&
                        s.HookOrder == 10003));
            Assert.Equal(1,
                bindingSourceProcessorStub.HookBindings.Count(
                    s =>
                        s.HookType == HookType.AfterTestRun && s.Method.Name == "AfterOrderTenThousandAnd4" &&
                        s.HookOrder == 10004));
        }
        
         [Fact]
        public void ShouldFindStepArgumentTransformations_WithSpecifiedOrder()
        {
            var builder = CreateSut();

            BuildCompleteBindingFromType(builder, typeof (StepTransformationExample));

            Assert.Single(
                bindingSourceProcessorStub.StepArgumentTransformationBindings,
                sat =>
                    sat.Method.Name == nameof(StepTransformationExample.Transform) && sat.Order == StepArgumentTransformationAttribute.DefaultOrder);
            
            Assert.Single(
                bindingSourceProcessorStub.StepArgumentTransformationBindings,
                sat =>
                    sat.Method.Name == nameof(StepTransformationExample.TransformWithRegexAndOrder) && sat.Order == 5);
            
            Assert.Single(
                bindingSourceProcessorStub.StepArgumentTransformationBindings,
                sat =>
                    sat.Method.Name == nameof(StepTransformationExample.TransformWithOrderAndWithoutRegex) && sat.Order == 10);
        }

        [Fact]
        public void ShouldFindExampleConverter()
        {
            var builder = CreateSut();

            BuildCompleteBindingFromAssembly(builder);
            Assert.Equal(1,
                bindingSourceProcessorStub.StepArgumentTransformationBindings.Count(
                    s =>
                        s.Regex != null && s.Regex.Match("BindingRegistryTests").Success &&
                        s.Regex.Match("").Success == false));
        }

        private static void BuildCompleteBindingFromAssembly(RuntimeBindingRegistryBuilder builder)
        {
            builder.BuildBindingsFromAssembly(Assembly.GetExecutingAssembly());
            builder.BuildingCompleted();
        }

        private static void BuildCompleteBindingFromType(RuntimeBindingRegistryBuilder builder, Type type)
        {
            builder.BuildBindingsFromType(type);
            builder.BuildingCompleted();
        }

        [Fact]
        public void ShouldFindScopedExampleConverter()
        {
            var builder = CreateSut();

            BuildCompleteBindingFromAssembly(builder);

            Assert.Equal(2,
                         bindingSourceProcessorStub.StepDefinitionBindings.Count(
                             s =>
                                 s.StepDefinitionType == StepDefinitionType.Then &&
                                 s.Regex.Match("SpecificBindingRegistryTests").Success && s.IsScoped));
            Assert.Equal(0,
                bindingSourceProcessorStub.StepDefinitionBindings.Count(
                    s =>
                        s.StepDefinitionType == StepDefinitionType.Then &&
                        s.Regex.Match("SpecificBindingRegistryTests").Success && s.IsScoped == false));
        }

        [Fact]
        public void ShouldFindScopedHook_WithCtorArg()
        {
            var builder = CreateSut();

            BuildCompleteBindingFromAssembly(builder);

            Assert.Equal(1,
                bindingSourceProcessorStub.HookBindings.Count(s => s.Method.Name == "Tag2BeforeScenario" && s.IsScoped));
        }

        [Fact]
        public void ShouldFindScopedHook_WithMultipleCtorArg()
        {
            var builder = CreateSut();

            BuildCompleteBindingFromAssembly(builder);

            Assert.Equal(2,
                bindingSourceProcessorStub.HookBindings.Count(s => s.Method.Name == "Tag34BeforeScenario" && s.IsScoped));
        }

        [Fact]
        public void ShouldFindScopedHook_WithScopeAttribute()
        {
            var builder = CreateSut();

            BuildCompleteBindingFromAssembly(builder);

            Assert.Equal(1,
                bindingSourceProcessorStub.HookBindings.Count(s => s.Method.Name == "Tag1BeforeScenario" && s.IsScoped));
        }

        [Fact]
        public void ShouldFindStepDefinitionsWithStepDefinitionAttributes()
        {
            var builder = CreateSut();

            BuildCompleteBindingFromType(builder, typeof(BindingClassWithStepDefinitionAttributes));

            Assert.Equal(3, bindingSourceProcessorStub.StepDefinitionBindings.Count);
            Assert.Equal(1, bindingSourceProcessorStub.StepDefinitionBindings.Count(b => b.StepDefinitionType == StepDefinitionType.Given));
            Assert.Equal(1, bindingSourceProcessorStub.StepDefinitionBindings.Count(b => b.StepDefinitionType == StepDefinitionType.When));
            Assert.Equal(1, bindingSourceProcessorStub.StepDefinitionBindings.Count(b => b.StepDefinitionType == StepDefinitionType.Then));
        }

        [Fact]
        public void ShouldFindStepDefinitionsWithTranslatedAttributes()
        {
            var builder = CreateSut();

            BuildCompleteBindingFromType(builder, typeof(BindingClassWithTranslatedStepDefinitionAttributes));

            Assert.Equal(3, bindingSourceProcessorStub.StepDefinitionBindings.Count);
            Assert.Equal(1, bindingSourceProcessorStub.StepDefinitionBindings.Count(b => b.StepDefinitionType == StepDefinitionType.Given));
            Assert.Equal(1, bindingSourceProcessorStub.StepDefinitionBindings.Count(b => b.StepDefinitionType == StepDefinitionType.When));
            Assert.Equal(1, bindingSourceProcessorStub.StepDefinitionBindings.Count(b => b.StepDefinitionType == StepDefinitionType.Then));
        }

        [Fact]
        public void ShouldFindStepDefinitionsWithCustomAttribute()
        {
            var builder = CreateSut();

            BuildCompleteBindingFromType(builder, typeof(BindingClassWithCustomStepDefinitionAttribute));

            Assert.Equal(2, bindingSourceProcessorStub.StepDefinitionBindings.Count);
            Assert.Equal(1, bindingSourceProcessorStub.StepDefinitionBindings.Count(b => b.StepDefinitionType == StepDefinitionType.Given));
            Assert.Equal(1, bindingSourceProcessorStub.StepDefinitionBindings.Count(b => b.StepDefinitionType == StepDefinitionType.When));
            Assert.Equal(0, bindingSourceProcessorStub.StepDefinitionBindings.Count(b => b.StepDefinitionType == StepDefinitionType.Then));
        }
    }
}
