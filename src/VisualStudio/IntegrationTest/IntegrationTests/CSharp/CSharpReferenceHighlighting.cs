﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editor.Implementation.ReferenceHighlighting;
using Microsoft.CodeAnalysis.Shared.TestHooks;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.IntegrationTest.Utilities;
using Microsoft.VisualStudio.IntegrationTest.Utilities.Input;
using Roslyn.Test.Utilities;
using Xunit;

namespace Roslyn.VisualStudio.IntegrationTests.CSharp
{
    [Collection(nameof(SharedIntegrationHostFixture))]
    public class CSharpReferenceHighlighting : AbstractEditorTest
    {
        protected override string LanguageName => LanguageNames.CSharp;

        public CSharpReferenceHighlighting(VisualStudioInstanceFactory instanceFactory)
            : base(instanceFactory, nameof(CSharpReferenceHighlighting))
        {
        }

        [Fact, Trait(Traits.Feature, Traits.Features.Classification)]
        public void Highlighting()
        {
            var markup = @"
class {|definition:C|}
{
    void M<T>({|reference:C|} c) where T : {|reference:C|}
    {
        {|reference:C|} c = new {|reference:C|}();
    }
}";
            Test.Utilities.MarkupTestFile.GetSpans(markup, out var text, out IDictionary<string, ImmutableArray<TextSpan>> spans);
            VisualStudio.Editor.SetText(text);
            Verify("C", spans);

            // Verify tags disappear
            VerifyNone("void");
        }

        [Fact, Trait(Traits.Feature, Traits.Features.Classification)]
        public void WrittenReference()
        {
            var markup = @"
class C
{
    void M()
    {
        int {|definition:x|};
        {|writtenreference:x|} = 3;
    }
}";
            Test.Utilities.MarkupTestFile.GetSpans(markup, out var text, out IDictionary<string, ImmutableArray<TextSpan>> spans);
            VisualStudio.Editor.SetText(text);
            Verify("x", spans);

            // Verify tags disappear
            VerifyNone("void");
        }

        [Fact, Trait(Traits.Feature, Traits.Features.Classification)]
        public void Navigation()
        {
            var text = @"
class C
{
   void M()
    {
        int x;
        x = 3;
    }
}";
            VisualStudio.Editor.SetText(text);
            VisualStudio.Editor.PlaceCaret("x");
            VisualStudio.Workspace.WaitForAsyncOperations(FeatureAttribute.ReferenceHighlighting);
            VisualStudio.ExecuteCommand("Edit.NextHighlightedReference");
            VisualStudio.Workspace.WaitForAsyncOperations(FeatureAttribute.ReferenceHighlighting);
            VisualStudio.Editor.Verify.CurrentLineText("x$$ = 3;", assertCaretPosition: true, trimWhitespace: true);
        }

        private void Verify(string marker, IDictionary<string, ImmutableArray<TextSpan>> spans)
        {
            VisualStudio.Editor.PlaceCaret(marker, charsOffset: -1);
            VisualStudio.Workspace.WaitForAsyncOperations(string.Concat(
               FeatureAttribute.SolutionCrawler,
               FeatureAttribute.DiagnosticService,
               FeatureAttribute.Classification,
               FeatureAttribute.ReferenceHighlighting));
            AssertEx.SetEqual(spans["definition"], VisualStudio.Editor.GetTagSpans(DefinitionHighlightTag.TagId));

            if (spans.ContainsKey("reference"))
            {
                AssertEx.SetEqual(spans["reference"], VisualStudio.Editor.GetTagSpans(ReferenceHighlightTag.TagId));
            }

            if (spans.ContainsKey("writtenreference"))
            {
                AssertEx.SetEqual(spans["writtenreference"], VisualStudio.Editor.GetTagSpans(WrittenReferenceHighlightTag.TagId));
            }
        }

        private void VerifyNone(string marker)
        {
            VisualStudio.Editor.PlaceCaret(marker, charsOffset: -1);
            VisualStudio.Workspace.WaitForAsyncOperations(string.Concat(
               FeatureAttribute.SolutionCrawler,
               FeatureAttribute.DiagnosticService,
               FeatureAttribute.Classification,
               FeatureAttribute.ReferenceHighlighting));

            Assert.Empty(VisualStudio.Editor.GetTagSpans(ReferenceHighlightTag.TagId));
            Assert.Empty(VisualStudio.Editor.GetTagSpans(DefinitionHighlightTag.TagId));
        }
    }
}