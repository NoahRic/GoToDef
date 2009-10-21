using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.ApplicationModel.Environments;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Text.Editor;
using System.Windows.Input;
using System.Windows;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace GoToDef
{
    [Export(typeof(IKeyProcessorProvider))]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    [ContentType("code")]
    [Name("ControlKeyProcessor")]
    [Order(Before = "VisualStudioKeyboardProcessor")]
    internal sealed class GoToDefKeyProcessorProvider : IKeyProcessorProvider
    {
        public KeyProcessor GetAssociatedProcessor(IWpfTextViewHost wpfTextViewHost)
        {
            ITextView view = wpfTextViewHost.TextView;
            return view.Properties.GetOrCreateSingletonProperty(typeof(GoToDefKeyProcessor),
                                                                () => new GoToDefKeyProcessor(CtrlKeyState.GetStateForView(view)));
        }
    }

    /// <summary>
    /// The state of the control key for a given view, which is kept up-to-date by a combination of the
    /// key processor and the mouse process
    /// </summary>
    internal sealed class CtrlKeyState
    {
        internal static CtrlKeyState GetStateForView(ITextView view)
        {
            return view.Properties.GetOrCreateSingletonProperty(typeof(CtrlKeyState), () => new CtrlKeyState());
        }

        bool _enabled = false;

        internal bool Enabled
        {
            get 
            {
                // Check and see if ctrl is down but we missed it somehow.
                bool ctrlDown = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                if (ctrlDown != _enabled)
                    Enabled = ctrlDown;

                return _enabled;
            }
            set 
            {
                bool oldVal = _enabled;
                _enabled = value;
                if (oldVal != _enabled)
                {
                    var temp = CtrlKeyStateChanged;
                    if (temp != null)
                        temp(this, new EventArgs());
                }
            }
        }
        
        internal event EventHandler<EventArgs> CtrlKeyStateChanged;
    }

    /// <summary>
    /// Listen for the control key being pressed or released to update the CtrlKeyStateChanged for a view.
    /// </summary>
    internal sealed class GoToDefKeyProcessor : KeyProcessor
    {
        CtrlKeyState _state;

        public GoToDefKeyProcessor(CtrlKeyState state)
        {
            _state = state;
        }
        
        void UpdateState(KeyEventArgs args)
        {
            _state.Enabled = (args.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0;
        }

        public override void PreviewKeyDown(KeyEventArgs args)
        {
            UpdateState(args);
        }

        public override void  PreviewKeyUp(KeyEventArgs args)
        {
            UpdateState(args);
        }
    }

    [Export(typeof(IMouseProcessorProvider))]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    [ContentType("code")]
    [Order(Before = "WordSelectionMouseProcessorProvider")]
    internal sealed class GoToDefMouseHandlerProvider : IMouseProcessorProvider
    {
        [Import]
        internal IClassifierAggregatorService AggregatorFactory;

        [Import]
        internal ITextStructureNavigatorSelectorService NavigatorService;

        [Import]
        internal IVsEditorAdaptersFactoryService AdaptersFactory;

        public IMouseProcessor GetAssociatedProcessor(IWpfTextViewHost wpfTextViewHost)
        {
            var buffer = wpfTextViewHost.TextView.TextBuffer;
            var environment = new StandardEnvironment();

            IOleCommandTarget shellCommandDispatcher = GetShellCommandDispatcher(wpfTextViewHost.TextView);

            if (shellCommandDispatcher == null)
                return null;

            return new GoToDefMouseHandler(wpfTextViewHost.TextView,
                                           shellCommandDispatcher,
                                           AggregatorFactory.GetClassifier(buffer, environment),
                                           NavigatorService.GetTextStructureNavigator(buffer, environment),
                                           CtrlKeyState.GetStateForView(wpfTextViewHost.TextView));
        }

        #region Private helpers

        /// <summary>
        /// Get the SUIHostCommandDispatcher from the shell.  This method is rather ugly, and will (hopefully) be cleaned up
        /// slightly whenever [Import]ing an IServiceProvider is available.
        /// </summary>
        /// <param name="view"></param>
        /// <returns></returns>
        IOleCommandTarget GetShellCommandDispatcher(ITextView view)
        {
            IOleCommandTarget shellCommandDispatcher;

            var vsBuffer = AdaptersFactory.GetBufferAdapter(view.TextBuffer);
            if (vsBuffer == null)
                return null;

            Guid guidServiceProvider = VSConstants.IID_IUnknown;
            IObjectWithSite objectWithSite = vsBuffer as IObjectWithSite;
            IntPtr ptrServiceProvider = IntPtr.Zero;
            objectWithSite.GetSite(ref guidServiceProvider, out ptrServiceProvider);

            Microsoft.VisualStudio.OLE.Interop.IServiceProvider serviceProvider = (Microsoft.VisualStudio.OLE.Interop.IServiceProvider)Marshal.GetObjectForIUnknown(ptrServiceProvider);
            Guid guidService = typeof(SUIHostCommandDispatcher).GUID;
            Guid guidInterface = typeof(IOleCommandTarget).GUID;
            IntPtr ptrObject = IntPtr.Zero;

            int hr = serviceProvider.QueryService(ref guidService, ref guidInterface, out ptrObject);
            if (ErrorHandler.Failed(hr) || ptrObject == IntPtr.Zero)
                return null;

            shellCommandDispatcher = (IOleCommandTarget)Marshal.GetObjectForIUnknown(ptrObject);

            Marshal.Release(ptrObject);

            return shellCommandDispatcher;
        }

        #endregion
    }

    /// <summary>
    /// Handle ctrl+click on valid elements to send GoToDefinition to the shell.  Also handle mouse moves
    /// (when control is pressed) to highlight references for which GoToDefinition will (likely) be valid.
    /// </summary>
    internal sealed class GoToDefMouseHandler : MouseProcessorBase
    {
        IWpfTextView _view;
        CtrlKeyState _state;
        IClassifier _aggregator;
        ITextStructureNavigator _navigator;
        IOleCommandTarget _commandTarget;

        public GoToDefMouseHandler(IWpfTextView view, IOleCommandTarget commandTarget, IClassifier aggregator, 
                                   ITextStructureNavigator navigator, CtrlKeyState state)
        {
            _view = view;
            _commandTarget = commandTarget;
            _state = state;
            _aggregator = aggregator;
            _navigator = navigator;

            _state.CtrlKeyStateChanged += (sender, args) =>
                {
                    if (_state.Enabled)
                        this.TryHighlightItemUnderMouse(RelativeToView(Mouse.PrimaryDevice.GetPosition(_view.VisualElement)));
                    else
                        this.SetHighlightSpan(null);
                };
        }

        #region Mouse processor overrides

        public override void PostprocessMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (_state.Enabled)
            {
                _state.Enabled = false;
                this.SetHighlightSpan(null);
                this.DispatchGoToDef();
            }
        }

        public override void PreprocessMouseMove(MouseEventArgs e)
        {
            if (_state.Enabled)
            {
                TryHighlightItemUnderMouse(RelativeToView(e.GetPosition(_view.VisualElement)));
            }

            // Don't mark the event as handled, so other mouse processors have a chance to do their work
            // (such as clicking+dragging to select text)
        }

        #endregion

        #region Private helpers

        Point RelativeToView(Point position)
        {
            return new Point(position.X + _view.ViewportLeft, position.Y + _view.ViewportTop);
        }

        bool TryHighlightItemUnderMouse(Point position)
        {
            bool updated = false;

            try
            {
                var line = _view.TextViewLines.GetTextViewLineContainingYCoordinate(position.Y);
                if (line == null)
                    return false;

                var bufferPosition = line.GetBufferPositionFromXCoordinate(position.X);

                if (!bufferPosition.HasValue)
                    return false;

                var extent = _navigator.GetExtentOfWord(bufferPosition.Value);
                if (!extent.IsSignificant)
                    return false;

                // For C#, we ignore namespaces after using statements - GoToDef will fail for those
                if (_view.TextBuffer.ContentType.IsOfType("csharp"))
                {
                    string lineText = bufferPosition.Value.GetContainingLine().GetText().Trim();
                    if (lineText.StartsWith("using", StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                //  Now, check for valid classification type.  C# and C++ (at least) classify the things we are interested
                // in as either "identifier" or "user types" (though "identifier" will yield some false positives).  VB, unfortunately,
                // doesn't classify identifiers.
                foreach (var classification in _aggregator.GetClassificationSpans(extent.Span))
                {
                    var name = classification.ClassificationType.Classification.ToLower();
                    if ((name.Contains("identifier") || name.Contains("user types")) &&
                        SetHighlightSpan(classification.Span))
                    {
                        updated = true;
                        return true;
                    }
                }

                // No update occurred, so return false
                return false;
            }
            finally
            {
                if (!updated)
                    SetHighlightSpan(null);
            }
        }


        bool SetHighlightSpan(SnapshotSpan? span)
        {
            var classifier = UnderlineClassifierProvider.GetClassifierForView(_view);
            if (classifier != null)
            {
                if (span.HasValue)
                    Mouse.OverrideCursor = Cursors.Hand;
                else
                    Mouse.OverrideCursor = null;

                classifier.SetUnderlineSpan(span);
                return true;
            }

            return false;
        }

        bool DispatchGoToDef()
        {
            int hr = _commandTarget.Exec(ref VsMenus.guidStandardCommandSet97,
                                         (uint)VSConstants.VSStd97CmdID.GotoDefn,
                                         (uint)OLECMDEXECOPT.OLECMDEXECOPT_DODEFAULT,
                                         System.IntPtr.Zero,
                                         System.IntPtr.Zero);
            return ErrorHandler.Succeeded(hr);
        }

        #endregion
    }
}