using System.CodeDom;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Reqnroll.BoDi;
using Reqnroll.Generator.CodeDom;

namespace Reqnroll.Generator.UnitTestProvider
{
    [SuppressMessage("ReSharper", "BitwiseOperatorOnEnumWithoutFlags")]
    public class NUnit3TestGeneratorProvider : IUnitTestGeneratorProvider
    {
        protected internal const string TESTFIXTURESETUP_ATTR_NUNIT3 = "NUnit.Framework.OneTimeSetUpAttribute";
        protected internal const string TESTFIXTURETEARDOWN_ATTR_NUNIT3 = "NUnit.Framework.OneTimeTearDownAttribute";
        protected internal const string NONPARALLELIZABLE_ATTR = "NUnit.Framework.NonParallelizableAttribute";
        protected internal const string TESTFIXTURE_ATTR = "NUnit.Framework.TestFixtureAttribute";
        protected internal const string FIXTURELIFECYCLE_ATTR = "NUnit.Framework.FixtureLifeCycleAttribute";
        protected internal const string LIFECYCLE_CLASS = "NUnit.Framework.LifeCycle";
        protected internal const string LIFECYCLE_INSTANCEPERTESTCASE = "InstancePerTestCase";
        protected internal const string TEST_ATTR = "NUnit.Framework.TestAttribute";
        protected internal const string ROW_ATTR = "NUnit.Framework.TestCaseAttribute";
        protected internal const string CATEGORY_ATTR = "NUnit.Framework.CategoryAttribute";
        protected internal const string TESTSETUP_ATTR = "NUnit.Framework.SetUpAttribute";
        protected internal const string TESTTEARDOWN_ATTR = "NUnit.Framework.TearDownAttribute";
        protected internal const string IGNORE_ATTR = "NUnit.Framework.IgnoreAttribute";
        protected internal const string DESCRIPTION_ATTR = "NUnit.Framework.DescriptionAttribute";
        protected internal const string TESTCONTEXT_TYPE = "NUnit.Framework.TestContext";
        protected internal const string TESTCONTEXT_INSTANCE = "NUnit.Framework.TestContext.CurrentContext";
        protected internal const string TESTCONTEXT_WORKERID_PROPERTY = "WorkerId";

        public NUnit3TestGeneratorProvider(CodeDomHelper codeDomHelper)
        {
            CodeDomHelper = codeDomHelper;
        }

        protected CodeDomHelper CodeDomHelper { get; set; }

        public bool GenerateParallelCodeForFeature { get; set; }

        public virtual UnitTestGeneratorTraits GetTraits()
        {
            return UnitTestGeneratorTraits.RowTests | UnitTestGeneratorTraits.ParallelExecution;
        }

        public virtual void SetTestClassIgnore(TestClassGenerationContext generationContext)
        {
            CodeDomHelper.AddAttribute(generationContext.TestClass, IGNORE_ATTR, "Ignored feature");
        }

        public virtual void SetTestMethodIgnore(TestClassGenerationContext generationContext, CodeMemberMethod testMethod)
        {
            CodeDomHelper.AddAttribute(testMethod, IGNORE_ATTR, "Ignored scenario");
        }

        public virtual void SetTestClassInitializeMethod(TestClassGenerationContext generationContext)
        {
            generationContext.TestClassInitializeMethod.Attributes |= MemberAttributes.Static;
            CodeDomHelper.AddAttribute(generationContext.TestClassInitializeMethod, TESTFIXTURESETUP_ATTR_NUNIT3);
        }

        public virtual void SetTestClassCleanupMethod(TestClassGenerationContext generationContext)
        {
            generationContext.TestClassCleanupMethod.Attributes |= MemberAttributes.Static;
            CodeDomHelper.AddAttribute(generationContext.TestClassCleanupMethod, TESTFIXTURETEARDOWN_ATTR_NUNIT3);
        }

        public virtual void SetTestClassNonParallelizable(TestClassGenerationContext generationContext)
        {
            CodeDomHelper.AddAttribute(generationContext.TestClass, NONPARALLELIZABLE_ATTR);
        }

        public void SetTestClass(TestClassGenerationContext generationContext, string featureTitle, string featureDescription)
        {
            CodeDomHelper.AddAttribute(generationContext.TestClass, TESTFIXTURE_ATTR);
            CodeDomHelper.AddAttribute(generationContext.TestClass, DESCRIPTION_ATTR, featureTitle);
            CodeDomHelper.AddAttribute(generationContext.TestClass, FIXTURELIFECYCLE_ATTR, 
                new CodeAttributeArgument(
                    new CodeSnippetExpression(CodeDomHelper.GetGlobalizedName($"{LIFECYCLE_CLASS}.{LIFECYCLE_INSTANCEPERTESTCASE}"))));
        }

        public void SetTestClassCategories(TestClassGenerationContext generationContext, IEnumerable<string> featureCategories)
        {
            CodeDomHelper.AddAttributeForEachValue(generationContext.TestClass, CATEGORY_ATTR, featureCategories);
        }

        public virtual void FinalizeTestClass(TestClassGenerationContext generationContext)
        {
            // testRunner.ScenarioContext.ScenarioContainer.RegisterInstanceAs<NUnit.Framework.TestContext>(NUnit.Framework.TestContext.CurrentContext);
            generationContext.ScenarioInitializeMethod.Statements.Add(
                new CodeMethodInvokeExpression(
                    new CodeMethodReferenceExpression(
                        new CodePropertyReferenceExpression(
                            new CodePropertyReferenceExpression(
                                new CodeFieldReferenceExpression(null, generationContext.TestRunnerField.Name),
                                nameof(ScenarioContext)),
                            nameof(ScenarioContext.ScenarioContainer)),
                        nameof(IObjectContainer.RegisterInstanceAs),
                        new CodeTypeReference(TESTCONTEXT_TYPE, CodeTypeReferenceOptions.GlobalReference)),
                    GetTestContextExpression()));
        }

        private CodeExpression GetTestContextExpression() => new CodeVariableReferenceExpression(CodeDomHelper.GetGlobalizedName(TESTCONTEXT_INSTANCE));

        public void SetTestInitializeMethod(TestClassGenerationContext generationContext)
        {
            CodeDomHelper.AddAttribute(generationContext.TestInitializeMethod, TESTSETUP_ATTR);
        }

        public void SetTestCleanupMethod(TestClassGenerationContext generationContext)
        {
            CodeDomHelper.AddAttribute(generationContext.TestCleanupMethod, TESTTEARDOWN_ATTR);
        }

        public virtual void SetTestMethod(TestClassGenerationContext generationContext, CodeMemberMethod testMethod, string friendlyTestName)
        {
            CodeDomHelper.AddAttribute(testMethod, TEST_ATTR);
            CodeDomHelper.AddAttribute(testMethod, DESCRIPTION_ATTR, friendlyTestName);
        }

        public virtual void SetTestMethodCategories(TestClassGenerationContext generationContext, CodeMemberMethod testMethod, IEnumerable<string> scenarioCategories)
        {
            CodeDomHelper.AddAttributeForEachValue(testMethod, CATEGORY_ATTR, scenarioCategories);
        }

        public virtual void SetRowTest(TestClassGenerationContext generationContext, CodeMemberMethod testMethod, string scenarioTitle)
        {
            SetTestMethod(generationContext, testMethod, scenarioTitle);
        }

        public void SetRow(TestClassGenerationContext generationContext, CodeMemberMethod testMethod, IEnumerable<string> arguments, IEnumerable<string> tags, bool isIgnored)
        {
            var args = arguments.Select(
                arg => new CodeAttributeArgument(new CodePrimitiveExpression(arg))).ToList();

            var tagsArray = tags.ToArray();

            // addressing ReSharper bug: TestCase attribute with empty string[] param causes inconclusive result - https://youtrack.jetbrains.com/issue/RSRP-279138
            bool hasExampleTags = tagsArray.Any();
            var exampleTagExpressionList = tagsArray.Select(t => (CodeExpression)new CodePrimitiveExpression(t));
            var exampleTagsExpression = hasExampleTags
                ? new CodeArrayCreateExpression(typeof(string[]), exampleTagExpressionList.ToArray())
                : (CodeExpression) new CodePrimitiveExpression(null);
                
            args.Add(new CodeAttributeArgument(exampleTagsExpression));

            // adds 'Category' named parameter so that NUnit also understands that this test case belongs to the given categories
            if (hasExampleTags)
            {
                CodeExpression exampleTagsStringExpr = new CodePrimitiveExpression(string.Join(",", tagsArray));
                args.Add(new CodeAttributeArgument("Category", exampleTagsStringExpr));
            }

            if (isIgnored)
                args.Add(new CodeAttributeArgument("IgnoreReason", new CodePrimitiveExpression("Ignored by @ignore tag")));

            CodeDomHelper.AddAttribute(testMethod, ROW_ATTR, args.ToArray());
        }

        public void SetTestMethodAsRow(TestClassGenerationContext generationContext, CodeMemberMethod testMethod, string scenarioTitle, string exampleSetName, string variantName, IEnumerable<KeyValuePair<string, string>> arguments)
        {
            // doing nothing since we support RowTest
        }

        public void MarkCodeMethodInvokeExpressionAsAwait(CodeMethodInvokeExpression expression)
        {
            CodeDomHelper.MarkCodeMethodInvokeExpressionAsAwait(expression);
        }
    }
}
