using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.ApplicationModel.Environments;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Text.Editor;

namespace GoToDef
{
    #region Classification type/format exports

    internal static class UnderlineClassificationExports
    {
        [Export(typeof(ClassificationTypeDefinition))]
        [Name("UnderlineClassification")]
        internal static ClassificationTypeDefinition underlineClassificationType;
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = "UnderlineClassification")]
    [Name("UnderlineClassificationFormat")]
    [DisplayName("Underline")]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class UnderlineFormatDefinition : ClassificationFormatDefinition
    {
        public UnderlineFormatDefinition()
        {
            this.TextDecorations = System.Windows.TextDecorations.Underline;
            this.ForegroundColor = Colors.Blue;
        }
    }

    #endregion

    #region Provider definition
    [Export(typeof(IClassifierProvider))]
    [ContentType("text")]
    internal class UnderlineClassifierProvider : IClassifierProvider
    {
        [Import]
        internal IClassificationTypeRegistryService ClassificationRegistry;

        public IClassifier GetClassifier(ITextBuffer buffer, IEnvironment context)
        {
            // We want to provide classifications for a specific view, since this is an interaction with the mouse over
            // a specific view.
            object textViewObject;
            if (!context.GetFromBindings(TextViewUtil.TextViewVariable, out textViewObject))
                return null;

            ITextView textView = textViewObject as ITextView;
            if (textView == null)
                return null;

            if (textView.TextBuffer != buffer)
                return null;

            // Try to get or create a classifier for the given view
            IClassifier classifier = GetClassifierForView(textView);
            if (classifier == null)
            {
                classifier = new UnderlineClassifier(textView,
                    ClassificationRegistry.GetClassificationType("UnderlineClassification"));
                textView.Properties.AddProperty(typeof(UnderlineClassifier), classifier);
            }

            return classifier;
        }

        internal static UnderlineClassifier GetClassifierForView(ITextView view)
        {
            UnderlineClassifier classifier;
            if (!view.Properties.TryGetProperty(typeof(UnderlineClassifier), out classifier))
                return null;
            else
                return classifier;
        }
    }
    #endregion

    internal class UnderlineClassifier : IClassifier
    {
        public event EventHandler<ClassificationChangedEventArgs> ClassificationChanged;

        IClassificationType _classificationType;
        ITextView _textView;
        CtrlKeyState _state;
        SnapshotSpan? _underlineSpan;

        internal UnderlineClassifier(ITextView textView, IClassificationType classificationType)
        {
            _textView = textView;
            _classificationType = classificationType;
            _underlineSpan = null;
        }

        #region Private helpers

        void SendEvent(SnapshotSpan span)
        {
            var temp = this.ClassificationChanged;
            if (temp != null)
                temp(this, new ClassificationChangedEventArgs(span));
        }

        #endregion

        #region UnderlineClassification public members

        public void SetUnderlineSpan(SnapshotSpan? span)
        {
            var oldSpan = _underlineSpan;
            _underlineSpan = span;

            if (!oldSpan.HasValue && !_underlineSpan.HasValue)
                return;

            if (!_underlineSpan.HasValue)
            {
                this.SendEvent(oldSpan.Value);
            }
            else
            {
                SnapshotSpan updateSpan = _underlineSpan.Value;
                if (oldSpan.HasValue)
                    updateSpan = new SnapshotSpan(updateSpan.Snapshot,
                        Span.FromBounds(Math.Min(updateSpan.Start, oldSpan.Value.Start),
                                        Math.Max(updateSpan.End, oldSpan.Value.End)));

                this.SendEvent(updateSpan);
            }
        }

        #endregion

        #region IClassifier Implementation

        public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
        {
            List<ClassificationSpan> classifications = new List<ClassificationSpan>(1);

            if (_underlineSpan.HasValue &&
                _underlineSpan.Value.TranslateTo(span.Snapshot, SpanTrackingMode.EdgeInclusive).IntersectsWith(span))
            {
                classifications.Add(new ClassificationSpan(_underlineSpan.Value, _classificationType));
            }

            return classifications;
        }
        #endregion
    }
}