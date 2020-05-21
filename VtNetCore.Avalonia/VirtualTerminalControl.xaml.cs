using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using VtNetCore.VirtualTerminal;
using VtNetCore.VirtualTerminal.Model;
using VtNetCore.XTermParser;

namespace VtNetCore.Avalonia
{
    public class VirtualTerminalControl : TemplatedControl
    {
        private CompositeDisposable _disposables;
        private CompositeDisposable _terminalDisposables;

        private int BlinkShowMs { get; set; } = 600;
        private int BlinkHideMs { get; set; } = 300;

        private string InputBuffer { get; set; } = "";

        DispatcherTimer blinkDispatcher;

        ScrollBar scrollBar;

        // Use Euclid's algorithm to calculate the
        // greatest common divisor (GCD) of two numbers.
        private long GCD(long a, long b)
        {
            a = Math.Abs(a);
            b = Math.Abs(b);

            // Pull out remainders.
            for (; ; )
            {
                long remainder = a % b;
                if (remainder == 0) return b;
                a = b;
                b = remainder;
            };
        }

        private static Color[] AttributeColors =
        {
            Color.FromArgb(255,0,0,0),        // Black
            Color.FromArgb(255,205,0,0),      // Red
            Color.FromArgb(255,0,205,0),      // Green
            Color.FromArgb(255,205,205,0),    // Yellow
            Color.FromArgb(255,0,0,205),      // Blue
            Color.FromArgb(255,205,0,205),    // Magenta
            Color.FromArgb(255,0,205,205),    // Cyan
            Color.FromArgb(255,205,205,205),  // White
            Color.FromArgb(255,127,127,127),     // Bright black
            Color.FromArgb(255,255,0,0),    // Bright red
            Color.FromArgb(255,0,255,0),    // Bright green
            Color.FromArgb(255,255,255,0),   // Bright yellow
            Color.FromArgb(255,92,92,255),    // Bright blue
            Color.FromArgb(255,255,0,255),   // Bright Magenta
            Color.FromArgb(255,0,255,255),   // Bright cyan
            Color.FromArgb(255,255,255,255),  // Bright white
        };

        private static SolidColorBrush[] AttributeBrushes =
        {
            new SolidColorBrush(AttributeColors[0]),
            new SolidColorBrush(AttributeColors[1]),
            new SolidColorBrush(AttributeColors[2]),
            new SolidColorBrush(AttributeColors[3]),
            new SolidColorBrush(AttributeColors[4]),
            new SolidColorBrush(AttributeColors[5]),
            new SolidColorBrush(AttributeColors[6]),
            new SolidColorBrush(AttributeColors[7]),
            new SolidColorBrush(AttributeColors[8]),
            new SolidColorBrush(AttributeColors[9]),
            new SolidColorBrush(AttributeColors[10]),
            new SolidColorBrush(AttributeColors[11]),
            new SolidColorBrush(AttributeColors[12]),
            new SolidColorBrush(AttributeColors[13]),
            new SolidColorBrush(AttributeColors[14]),
            new SolidColorBrush(AttributeColors[15]),
        };

        public double CharacterWidth { get; private set; } = -1;
        public double CharacterHeight { get; private set; } = -1;
        public int Columns { get; private set; } = -1;
        public int Rows { get; private set; } = -1;
        public DataConsumer Consumer { get; set; }

        private int viewTop = 0;
        public int ViewTop { 
            get => viewTop; 
            set 
            {
                viewTop = value;
                if(scrollBar != null) scrollBar.Value = ViewTop;
            }
        }
        public string WindowTitle { get; set; } = "Session";

        public bool ViewDebugging { get; set; }
        public bool DebugMouse { get; set; }
        public bool DebugSelect { get; set; }

        private char[] _rawText = new char[0];
        private int _rawTextLength = 0;
        private string _rawTextString = "";
        private bool _rawTextChanged = false;
        public DateTime TerminalIdleSince = DateTime.Now;

        public string RawText
        {
            get
            {
                if (_rawTextChanged)
                {
                    lock (_rawText)
                    {

                        _rawTextString = new string(_rawText, 0, _rawTextLength);
                        _rawTextChanged = false;
                    }
                }
                return _rawTextString;
            }
        }

        static VirtualTerminalControl()
        {
            AffectsRender<VirtualTerminalControl>(ConnectionProperty);
        }

        public VirtualTerminalControl()
        {
            blinkDispatcher = new DispatcherTimer();
            blinkDispatcher.Tick += (sender, e) => InvalidateVisual();
            blinkDispatcher.Interval = TimeSpan.FromMilliseconds(GCD(BlinkShowMs, BlinkHideMs));
            //blinkDispatcher.Start();

            this.GetObservable(TerminalProperty)
                .ObserveOn(AvaloniaScheduler.Instance)
                .Subscribe(terminal =>
                {
                    if(_terminalDisposables != null)
                    {
                        _terminalDisposables.Dispose();
                        _terminalDisposables = null;
                        Consumer = null;
                    }

                    Columns = -1;
                    Rows = -1;
                    InputBuffer = "";
                    TerminalIdleSince = DateTime.Now;
                    _rawTextChanged = false;
                    _rawTextString = "";
                    _rawTextLength = 0;
                    _rawText = new char[0];
                    ViewTop = 0;
                    CharacterHeight = -1;
                    CharacterWidth = -1;

                    if (terminal != null)
                    {
                        _terminalDisposables = new CompositeDisposable();
                        Consumer = new DataConsumer(terminal);

                        _terminalDisposables.Add(
                            Observable.FromEventPattern<SendDataEventArgs>(terminal, nameof(terminal.SendData)).Subscribe(e => OnSendData(e.EventArgs)));
                        
                        _terminalDisposables.Add(
                            Observable.FromEventPattern<TextEventArgs>(terminal, nameof(terminal.WindowTitleChanged))
                            .ObserveOn(AvaloniaScheduler.Instance)
                            .Subscribe(e => WindowTitle = e.EventArgs.Text));
                        
                        terminal.StoreRawText = true;
                    }
                });

            this.GetObservable(ConnectionProperty)
                .ObserveOn(AvaloniaScheduler.Instance)
                .Subscribe(connection =>
            {
                if (_disposables != null)
                {
                    _disposables.Dispose();
                    _disposables = null;
                }

                _disposables = new CompositeDisposable();

                if (connection != null)
                {
                    _disposables.Add(Observable.FromEventPattern<DataReceivedEventArgs>(connection, nameof(connection.DataReceived))
                        .ObserveOn(AvaloniaScheduler.Instance)
                        .Subscribe(args => OnDataReceived(args.EventArgs)));

                    connection.SetTerminalWindowSize(Columns, Rows, 800, 600);
                }
            });
        }

        protected override void OnTemplateApplied(TemplateAppliedEventArgs e)
        {
            base.OnTemplateApplied(e);
            scrollBar = e.NameScope.Find<ScrollBar>("ScrollBar");

            scrollBar.Scroll += (o, i) =>
            {
                SetScroll((int)i.NewValue);
            };
        }

        public static readonly StyledProperty<IConnection> ConnectionProperty =
            AvaloniaProperty.Register<VirtualTerminalControl, IConnection>(nameof(Connection));

        public IConnection Connection
        {
            get { return GetValue(ConnectionProperty); }
            set { SetValue(ConnectionProperty, value); }
        }

        public static readonly StyledProperty<VirtualTerminalController> TerminalProperty =
            AvaloniaProperty.Register<VirtualTerminalControl, VirtualTerminalController>(nameof(Terminal));

        public VirtualTerminalController Terminal
        {
            get => GetValue(TerminalProperty);
            set => SetValue(TerminalProperty, value);
        }

        public static readonly AvaloniaProperty<Thickness> TextPaddingProperty =
           AvaloniaProperty.Register<VirtualTerminalControl, Thickness>(nameof(TextPadding));

        public Thickness TextPadding
        {
            get => GetValue(TextPaddingProperty);
            set => SetValue(TextPaddingProperty, value);
        }

        protected override void OnGotFocus(GotFocusEventArgs e)
        {
            base.OnGotFocus(e);

            Terminal?.FocusIn();

            InvalidateVisual();
        }

        protected override void OnLostFocus(RoutedEventArgs e)
        {
            base.OnLostFocus(e);

            Terminal?.FocusOut();

            InvalidateVisual();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _terminalDisposables?.Dispose();
            _disposables?.Dispose();
            _terminalDisposables = null;
            _disposables = null;

            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            var ch = e.Text;

            // Since I get the same key twice in TerminalKeyDown and in CoreWindow_CharacterReceived
            // I lookup whether KeyPressed should handle the key here or there.
            var code = Terminal.GetKeySequence(ch, false, false);
            if (code == null)
                e.Handled = Terminal.KeyPressed(ch, false, false);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!Connected)
                return;

            var controlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            var shiftPressed = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

            if (controlPressed)
            {
                switch (e.Key)
                {
                    case Key.F10:
                        Consumer.SequenceDebugging = !Consumer.SequenceDebugging;
                        return;

                    case Key.F11:
                        ViewDebugging = !ViewDebugging;
                        InvalidateVisual();
                        return;

                    case Key.F12:
                        Terminal.Debugging = !Terminal.Debugging;
                        return;
                }
            }

            // Since I get the same key twice in TerminalKeyDown and in CoreWindow_CharacterReceived
            // I lookup whether KeyPressed should handle the key here or there.
            var code = Terminal.GetKeySequence(e.Key.ToString(), controlPressed, shiftPressed);
            if (code != null)
                e.Handled = Terminal.KeyPressed(e.Key.ToString(), controlPressed, shiftPressed);

            if (ViewTop != Terminal.ViewPort.TopRow)
            {
                ViewTop = Terminal.ViewPort.TopRow;
                InvalidateVisual();
            }
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            var pointer = e.GetPosition(this);

            var controlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);

            if (controlPressed)
            {
                double scale = 0.9 * (e.Delta.Y);

                var newFontSize = FontSize;
                if (scale < 0)
                    newFontSize *= Math.Abs(scale);
                else
                    newFontSize /= scale;

                if (newFontSize < 2)
                    newFontSize = 2;
                if (newFontSize > 20)
                    newFontSize = 20;

                if (newFontSize != FontSize)
                {
                    FontSize = newFontSize;

                    InvalidateVisual();
                }
            }
            else
            {               
                SetScroll((int)(ViewTop - e.Delta.Y * 2));                
            }
        }

        protected void SetScroll(int value)
        {
            int oldViewTop = ViewTop;

            ViewTop = value;

            if (ViewTop < 0)
                ViewTop = 0;
            else if (ViewTop > Terminal.ViewPort.TopRow)
                ViewTop = Terminal.ViewPort.TopRow;

            if (oldViewTop != ViewTop)
                InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (!(e.Source is VirtualTerminalControl)) return;

            var pointer = e.GetPosition(this);
            var position = ToPosition(pointer);

            var textPosition = position.OffsetBy(0, ViewTop);

            if (Connected && (Terminal.UseAllMouseTracking || Terminal.CellMotionMouseTracking) && position.Column >= 0 && position.Row >= 0 && position.Column < Columns && position.Row < Rows)
            {
                var controlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);
                var shiftPressed = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

                var button =
                    e.InputModifiers.HasFlag(InputModifiers.LeftMouseButton) ? 0 :
                        e.InputModifiers.HasFlag(InputModifiers.RightMouseButton) ? 1 :
                            e.InputModifiers.HasFlag(InputModifiers.MiddleMouseButton) ? 2 :
                            3;  // No button

                Terminal.MouseMove(position.Column, position.Row, button, controlPressed, shiftPressed);

                if (button == 3 && !Terminal.UseAllMouseTracking)
                    return;
            }

            if (MouseOver != null && MouseOver == position)
                return;

            MouseOver = position;

            if (e.InputModifiers.HasFlag(InputModifiers.LeftMouseButton))
            {
                TextRange newSelection;

                if (MousePressedAt != null && MousePressedAt != textPosition)
                {
                    if (MousePressedAt <= textPosition)
                    {
                        newSelection = new TextRange
                        {
                            Start = MousePressedAt,
                            End = textPosition.OffsetBy(-1, 0)
                        };
                    }
                    else
                    {
                        newSelection = new TextRange
                        {
                            Start = textPosition,
                            End = MousePressedAt
                        };
                    }

                    Selecting = true;

                    if (TextSelection != newSelection)
                    {
                        TextSelection = newSelection;

                        if (DebugSelect)
                            System.Diagnostics.Debug.WriteLine("Selection: " + TextSelection.ToString());

                        InvalidateVisual();
                    }
                }
            }

            if (DebugMouse)
                System.Diagnostics.Debug.WriteLine("Pointer Moved " + position.ToString());
        }

        protected override void OnPointerLeave(PointerEventArgs e)
        {
            MouseOver = null;

            if (DebugMouse)
                System.Diagnostics.Debug.WriteLine("TerminalPointerExited()");

            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var pointer = e.GetPosition(this);
            var position = ToPosition(pointer);

            var textPosition = position.OffsetBy(0, ViewTop);

            if (!Connected || (Connected && !Terminal.X10SendMouseXYOnButton && !Terminal.X11SendMouseXYOnButton && !Terminal.SgrMouseMode && !Terminal.CellMotionMouseTracking && !Terminal.UseAllMouseTracking))
            {
                if (e.InputModifiers.HasFlag(InputModifiers.LeftMouseButton))
                    MousePressedAt = textPosition;
                else if (e.InputModifiers.HasFlag(InputModifiers.RightMouseButton))
                    PasteClipboard();
            }

            if (Connected && position.Column >= 0 && position.Row >= 0 && position.Column < Columns && position.Row < Rows)
            {
                var controlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);
                var shiftPressed = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

                var button =
                    e.InputModifiers.HasFlag(InputModifiers.LeftMouseButton) ? 0 :
                        e.InputModifiers.HasFlag(InputModifiers.RightMouseButton) ? 1 :
                            2;  // Middle button

                Terminal.MousePress(position.Column, position.Row, button, controlPressed, shiftPressed);
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            var pointer = e.GetPosition(this);
            var position = ToPosition(pointer);
            var textPosition = position.OffsetBy(0, ViewTop);

            if (!e.InputModifiers.HasFlag(InputModifiers.LeftMouseButton))
            {
                if (Selecting)
                {
                    MousePressedAt = null;
                    Selecting = false;

                    if (DebugSelect)
                        System.Diagnostics.Debug.WriteLine("Captured : " + Terminal.GetText(TextSelection.Start.Column, TextSelection.Start.Row, TextSelection.End.Column, TextSelection.End.Row));

                    var captured = Terminal.GetText(TextSelection.Start.Column, TextSelection.Start.Row, TextSelection.End.Column, TextSelection.End.Row);

                    Application.Current.Clipboard.SetTextAsync(captured).GetAwaiter().GetResult();
                }
                else
                {
                    TextSelection = null;
                    InvalidateVisual();
                }
            }

            if (Connected && position.Column >= 0 && position.Row >= 0 && position.Column < Columns && position.Row < Rows)
            {
                var controlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);
                var shiftPressed = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

                Terminal.MouseRelease(position.Column, position.Row, controlPressed, shiftPressed);
            }
        }

        private void OnSendData(SendDataEventArgs e)
        {
            if (!Connected)
                return;

            var connection = Connection;

            Task.Run(() =>
            {
                connection.SendData(e.Data);
            });
        }

        public bool Connected
        {
            get { return Connection != null && Connection.IsConnected; }
        }

        private void OnDataReceived(DataReceivedEventArgs e)
        {
            lock (Terminal)
            {
                int oldTopRow = Terminal.ViewPort.TopRow;

                Consumer.Push(e.Data);

                if (Terminal.Changed)
                {
                    ProcessRawText();
                    Terminal.ClearChanges();

                    if (oldTopRow != Terminal.ViewPort.TopRow && oldTopRow >= ViewTop)
                        ViewTop = Terminal.ViewPort.TopRow;

                    InvalidateVisual();
                }

                TerminalIdleSince = DateTime.Now;
            }
        }

        private void ProcessRawText()
        {
            var incoming = Terminal.RawText;

            lock (_rawText)
            {
                if ((_rawTextLength + incoming.Length) > _rawText.Length)
                    Array.Resize(ref _rawText, _rawText.Length + 1000000);

                for (var i = 0; i < incoming.Length; i++)
                    _rawText[_rawTextLength++] = incoming[i];

                _rawTextChanged = true;
            }
        }

        private bool BlinkVisible()
        {
            var blinkCycle = BlinkShowMs + BlinkHideMs;

            return (DateTime.Now.Subtract(DateTime.MinValue).Milliseconds % blinkCycle) < BlinkHideMs;
        }

        public IBrush GetSolidColorBrush(string hex)
        {
            byte a = 255; // (byte)(Convert.ToUInt32(hex.Substring(0, 2), 16));
            byte r = (byte)(Convert.ToUInt32(hex.Substring(1, 2), 16));
            byte g = (byte)(Convert.ToUInt32(hex.Substring(3, 2), 16));
            byte b = (byte)(Convert.ToUInt32(hex.Substring(5, 2), 16));
            return new SolidColorBrush(Color.FromArgb(a, r, g, b));
        }

        private void PaintBackgroundLayer(DrawingContext context, List<VirtualTerminal.Layout.LayoutRow> spans)
        {
            if(spans == null)
            {
                return;
            }

            double lineY = 0;
            foreach (var textRow in spans)
            {
                using (context.PushPreTransform(Matrix.CreateScale(
                        (textRow.DoubleWidth ? 2.0 : 1.0),  // Scale double width
                        (textRow.DoubleHeightBottom | textRow.DoubleHeightTop ? 2.0 : 1.0) // Scale double high
                    )))
                {

                    var drawY =
                        (lineY - (textRow.DoubleHeightBottom ? CharacterHeight : 0)) *      // Offset position upwards for bottom of double high char
                        ((textRow.DoubleHeightBottom | textRow.DoubleHeightTop) ? 0.5 : 1.0); // Scale position for double height

                    double drawX = TextPadding.Left;
                    drawY += TextPadding.Top;
                    foreach (var textSpan in textRow.Spans)
                    {
                        var bounds =
                            new Rect(
                                drawX,
                                drawY,
                                CharacterWidth * (textSpan.Text.Length) + 0.9,
                                CharacterHeight + 0.9
                            );

                        context.FillRectangle(GetSolidColorBrush(textSpan.BackgroundColor), bounds);

                        drawX += CharacterWidth * (textSpan.Text.Length);
                    }

                    lineY += CharacterHeight;
                }
            }
        }

        private void PaintTextLayer(DrawingContext context, List<VirtualTerminal.Layout.LayoutRow> spans, Typeface textFormat, bool showBlink)
        {
            if(spans == null)
            {
                return;

            }
            var dipToDpiRatio = 96 / 96; // TODO read screen dpi.

            double lineY = 0;
            foreach (var textRow in spans)
            {
                using (context.PushPreTransform(Matrix.CreateScale(
                        (textRow.DoubleWidth ? 2.0 : 1.0),  // Scale double width
                        (textRow.DoubleHeightBottom | textRow.DoubleHeightTop ? 2.0 : 1.0) // Scale double high
                    )))
                {
                    var drawY =
                        (lineY - (textRow.DoubleHeightBottom ? CharacterHeight : 0)) *      // Offset position upwards for bottom of double high char
                        ((textRow.DoubleHeightBottom | textRow.DoubleHeightTop) ? 0.5 : 1.0); // Scale position for double height

                    double drawX = TextPadding.Left;
                    drawY += TextPadding.Top;
                    foreach (var textSpan in textRow.Spans)
                    {
                        var runWidth = CharacterWidth * (textSpan.Text.Length);

                        if (textSpan.Hidden || (textSpan.Blink && !showBlink))
                        {
                            drawX += runWidth;
                            continue;
                        }

                        var color = GetSolidColorBrush(textSpan.ForgroundColor);

                        var typeface = new Typeface(textFormat.FontFamily, FontSize , FontStyle.Normal, textSpan.Bold ? FontWeight.Bold : FontWeight.Light);

                        var textLayout = new FormattedText()
                        {
                            Text = textSpan.Text,
                            Typeface = typeface,
                        };

                        context.DrawText(color, new Point(drawX, drawY), textLayout);

                        // TODO : Come up with a better means of identifying line weight and offset
                        double underlineOffset = dipToDpiRatio * 1.07;

                        if (textSpan.Underline)
                        {
                            context.DrawLine(new Pen(color), new Point(drawX, drawY + underlineOffset), new Point(drawX + runWidth, drawY + underlineOffset));
                        }

                        drawX += CharacterWidth * (textSpan.Text.Length);
                    }

                    lineY += CharacterHeight;
                }
            }
        }

        private void PaintCursor(DrawingContext context, List<VirtualTerminal.Layout.LayoutRow> spans, Typeface textFormat, TextPosition cursorPosition, IBrush cursorColor)
        {
            var cursorY = cursorPosition.Row;

            if (cursorY >= 0 && spans != null && cursorY < spans.Count)
            {
                var textRow = spans[cursorY];

                using (context.PushPreTransform(Matrix.CreateTranslation(
                        1.0f,
                        (textRow.DoubleHeightBottom ? -CharacterHeight : 0)
                    ) *
                    Matrix.CreateScale(
                        (textRow.DoubleWidth ? 2.0 : 1.0),
                        (textRow.DoubleHeightBottom | textRow.DoubleHeightTop ? 2.0 : 1.0)
                    )))
                {

                    var drawX = cursorPosition.Column * CharacterWidth;
                    var drawY = (cursorY * CharacterHeight) * ((textRow.DoubleHeightBottom | textRow.DoubleHeightTop) ? 0.5 : 1.0);

                    var cursorRect = new Rect(
                        drawX + TextPadding.Left,
                        drawY + TextPadding.Top,
                        CharacterWidth,
                        CharacterHeight + 0.9
                    );

                    if (IsFocused)
                    {
                        context.FillRectangle(cursorColor, cursorRect);
                    }
                    else
                    {
                        context.DrawRectangle(new Pen(cursorColor), cursorRect);
                    }
                }
            }
        }

        public override void Render(DrawingContext context)
        {
            var textFormat =
                new Typeface(FontFamily, FontSize, FontStyle, FontWeight);            

            ProcessTextFormat(context, textFormat);

            var showBlink = BlinkVisible();

            List<VirtualTerminal.Layout.LayoutRow> spans = null;
            TextPosition cursorPosition = null;
            bool showCursor = false;
            IBrush cursorColor = Brushes.Green;

            scrollBar.Maximum = Terminal.ViewPort.TopRow;
            scrollBar.ViewportSize = Bounds.Height;

            if (Terminal != null)
            {
                lock (Terminal)
                {
                    spans = Terminal.ViewPort.GetPageSpans(ViewTop, Rows, Columns, TextSelection);
                    showCursor = Terminal.CursorState.ShowCursor;
                    cursorPosition = new TextPosition(Terminal.ViewPort.CursorPosition.Column, Terminal.ViewPort.CursorPosition.Row - ViewTop + Terminal.ViewPort.TopRow);
                    cursorColor = GetSolidColorBrush(Terminal.CursorState.Attributes.WebColor);
                }
            }

            PaintBackgroundLayer(context, spans);

            PaintTextLayer(context, spans, textFormat, showBlink);

            if (showCursor)
            {
                PaintCursor(context, spans, textFormat, cursorPosition, cursorColor);
            }

            if (ViewDebugging)
            {
                AnnotateView(context);
            }
        }

        private void AnnotateView(DrawingContext context)
        {
            var lineNumberFormat = new Typeface(FontFamily, FontSize, FontStyle, FontWeight);

            for (var i = 0; i < Rows; i++)
            {
                string s = i.ToString();
                var textLayout = new FormattedText
                {
                    Text = s.ToString(),
                    Typeface = lineNumberFormat,
                };

                var y = i * CharacterHeight;
                context.DrawLine(new Pen(Brushes.Beige), new Point(0, y), new Point(Bounds.Size.Width, y));
                context.DrawText(Brushes.Yellow, new Point((Bounds.Size.Width - (CharacterWidth / 2 * s.Length)), y), textLayout);

                s = (i + 1).ToString();

                textLayout = new FormattedText { Text = s.ToString(), Typeface = lineNumberFormat};
                context.DrawText(Brushes.Green, new Point((Bounds.Size.Width - (CharacterWidth / 2 * (s.Length + 3))), y), textLayout);
            }

            var bigText = Terminal.DebugText;
            var bigTextLayout = new FormattedText { Text = bigText, Typeface = lineNumberFormat};
            context.DrawText(Brushes.Yellow, new Point((Bounds.Size.Width - bigTextLayout.Bounds.Width - 100), 0), bigTextLayout);
        }

        private IBrush GetBackgroundBrush(TerminalAttribute attribute, bool invert)
        {
            var flip = Terminal.CursorState.ReverseVideoMode ^ attribute.Reverse ^ invert;

            if (flip)
            {
                if (attribute.ForegroundRgb == null)
                {
                    if (attribute.Bright)
                        return AttributeBrushes[(int)attribute.ForegroundColor + 8];

                    return AttributeBrushes[(int)attribute.ForegroundColor];
                }
                else
                    return new SolidColorBrush(Color.FromArgb(255, (byte)attribute.ForegroundRgb.Red, (byte)attribute.ForegroundRgb.Green, (byte)attribute.ForegroundRgb.Blue));
            }
            else
            {
                if (attribute.BackgroundRgb == null)
                    return AttributeBrushes[(int)attribute.BackgroundColor];
                else
                    return new SolidColorBrush(Color.FromArgb(255, (byte)attribute.BackgroundRgb.Red, (byte)attribute.BackgroundRgb.Green, (byte)attribute.BackgroundRgb.Blue));
            }
        }

        private IBrush GetForegroundBrush(TerminalAttribute attribute, bool invert)
        {
            var flip = Terminal.CursorState.ReverseVideoMode ^ attribute.Reverse ^ invert;

            if (flip)
            {
                if (attribute.BackgroundRgb == null)
                {
                    if (attribute.Bright)
                    {
                        return AttributeBrushes[(int)attribute.BackgroundColor + 8];
                    }

                    return AttributeBrushes[(int)attribute.BackgroundColor];
                }
                else
                    return new SolidColorBrush(Color.FromArgb(255, (byte)attribute.BackgroundRgb.Red, (byte)attribute.BackgroundRgb.Green, (byte)attribute.BackgroundRgb.Blue));
            }
            else
            {
                if (attribute.ForegroundRgb == null)
                {
                    if (attribute.Bright)
                    {
                        return AttributeBrushes[(int)attribute.ForegroundColor + 8];
                    }

                    return AttributeBrushes[(int)attribute.ForegroundColor];
                }
                else
                    return new SolidColorBrush(Color.FromArgb(255, (byte)attribute.ForegroundRgb.Red, (byte)attribute.ForegroundRgb.Green, (byte)attribute.ForegroundRgb.Blue));
            }
        }

        private void ProcessTextFormat(DrawingContext drawingSession, Typeface format)
        {
            var textLayout = new FormattedText
            {
                Text = "\u2560",
                Typeface = format,
                Wrapping = TextWrapping.NoWrap,
            };

            var size = textLayout.Bounds;

            if (CharacterWidth != size.Width || CharacterHeight != size.Height)
            {
                CharacterWidth = size.Width;
                CharacterHeight = size.Height;
            }

            int columns = Convert.ToInt32(Math.Floor((Bounds.Size.Width - TextPadding.Left - TextPadding.Right) / CharacterWidth));
            int rows = Convert.ToInt32(Math.Floor((Bounds.Size.Height - TextPadding.Top - TextPadding.Bottom) / CharacterHeight));
            if (Columns != columns || Rows != rows)
            {
                Columns = columns;
                Rows = rows;
                ResizeTerminal();

                if (Connection != null)
                    Connection.SetTerminalWindowSize(columns, rows, (int)Bounds.Width, (int)Bounds.Height);
            }
        }

        private void ResizeTerminal()
        {
            Terminal?.ResizeView(Columns, Rows);
        }

        TextPosition MouseOver { get; set; } = new TextPosition();

        TextRange TextSelection { get; set; }
        bool Selecting = false;

        private TextPosition ToPosition(Point point)
        {
            int overColumn = (int)Math.Floor(point.X / CharacterWidth);
            if (overColumn >= Columns)
                overColumn = Columns - 1;

            int overRow = (int)Math.Floor(point.Y / CharacterHeight);
            if (overRow >= Rows)
                overRow = Rows - 1;

            return new TextPosition { Column = overColumn, Row = overRow };
        }

        public TextPosition MousePressedAt { get; set; }

        private void PasteText(string text)
        {
            if (Connection == null)
                return;

            var buffer = Encoding.UTF8.GetBytes(text);

            var connection = Connection;

            Task.Run(() =>
            {
                connection.SendData(buffer);
            });
        }

        private async void PasteClipboard()
        {
            string text = await Application.Current.Clipboard.GetTextAsync();

            if (!string.IsNullOrEmpty(text))
            {
                PasteText(text);
            }
        }
    }
}
