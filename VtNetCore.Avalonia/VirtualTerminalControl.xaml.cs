using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Text;
using System.Threading.Tasks;
using VtNetCore.VirtualTerminal;
using VtNetCore.VirtualTerminal.Model;
using VtNetCore.XTermParser;

namespace VtNetCore.Avalonia
{
    public class VirtualTerminalControl : TemplatedControl
    {
        private int BlinkShowMs { get; set; } = 600;
        private int BlinkHideMs { get; set; } = 300;

        private string InputBuffer { get; set; } = "";

        DispatcherTimer blinkDispatcher;

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

        public IConnection Connection { get; set; }

        public VirtualTerminalController Terminal { get; set; } = new VirtualTerminalController();
        public DataConsumer Consumer { get; set; }
        public int ViewTop { get; set; } = 0;
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

        public VirtualTerminalControl()
        {
            Consumer = new DataConsumer(Terminal);

            Terminal.SendData += OnSendData;
            Terminal.WindowTitleChanged += OnWindowTitleChanged;
            Terminal.OnLog += OnLog;
            Terminal.StoreRawText = true;

            blinkDispatcher = new DispatcherTimer();
            blinkDispatcher.Tick += (sender, e) => InvalidateVisual();
            blinkDispatcher.Interval = TimeSpan.FromMilliseconds(Math.Min(150, GCD(BlinkShowMs, BlinkHideMs)));
            blinkDispatcher.Start();
        }

        protected override void OnGotFocus(GotFocusEventArgs e)
        {
            base.OnGotFocus(e);

            InvalidateVisual();
        }

        protected override void OnLostFocus(RoutedEventArgs e)
        {
            base.OnLostFocus(e);

            InvalidateVisual();
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

            var controlPressed = e.Modifiers.HasFlag(InputModifiers.Control);
            var shiftPressed = e.Modifiers.HasFlag(InputModifiers.Shift);

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
                Terminal.ViewPort.SetTopLine(ViewTop);
                InvalidateVisual();
            }
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            var pointer = e.GetPosition(this);

            var controlPressed = e.InputModifiers.HasFlag(InputModifiers.Control);

            if (controlPressed)
            {
                double scale = 0.9 * (e.Delta.Y / 120);

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
                int oldViewTop = ViewTop;

                ViewTop -= (int)(e.Delta.Y / 40);

                if (ViewTop < 0)
                    ViewTop = 0;
                else if (ViewTop > Terminal.ViewPort.TopRow)
                    ViewTop = Terminal.ViewPort.TopRow;

                if (oldViewTop != ViewTop)
                    InvalidateVisual();
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var pointer = e.GetPosition(this);
            var position = ToPosition(pointer);

            if (MouseOver != null && MouseOver == position)
                return;

            MouseOver = position;

            if (e.InputModifiers.HasFlag(InputModifiers.LeftMouseButton))
            {
                TextRange newSelection;
                var textPosition = position.OffsetBy(0, ViewTop);

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
            if (e.InputModifiers.HasFlag(InputModifiers.LeftMouseButton))
                MousePressedAt = textPosition;
            else if (e.InputModifiers.HasFlag(InputModifiers.RightMouseButton))
                PasteClipboard();

            if (Connected && position.Column >= 0 && position.Row >= 0 && position.Column < Columns && position.Row < Rows)
            {
                var controlPressed = e.InputModifiers.HasFlag(InputModifiers.Control);
                var shiftPressed = e.InputModifiers.HasFlag(InputModifiers.Shift);

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
                var controlPressed = e.InputModifiers.HasFlag(InputModifiers.Control);
                var shiftPressed = e.InputModifiers.HasFlag(InputModifiers.Shift);

                Terminal.MouseRelease(position.Column, position.Row, controlPressed, shiftPressed);
            }
        }

        private void OnLog(object sender, TextEventArgs e)
        {
            //LogText += (e.Text.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\n");
        }

        private void OnWindowTitleChanged(object sender, TextEventArgs e)
        {
            WindowTitle = e.Text;
        }

        private void OnSendData(object sender, SendDataEventArgs e)
        {
            if (!Connected)
                return;

            Task.Run(() =>
            {
                Connection.SendData(e.Data);
            });
        }

        public bool Connected
        {
            get { return Connection != null && Connection.IsConnected; }
        }

        public void Disconnect()
        {
            if (!Connected)
                return;

            Connection.Disconnect();
            Connection.DataReceived -= OnDataReceived;
            Connection = null;
        }

        public bool ConnectTo(IConnection connection)
        {
            if (Connected)
                return false;

            Connection = connection;
            Connection.SetTerminalWindowSize(Columns, Rows, 800, 600);

            Connection.DataReceived += OnDataReceived;

            var result = Connection.Connect();

            return result;
        }

        private void OnDataReceived(object sender, DataReceivedEventArgs e)
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

        public override void Render(DrawingContext context)
        {
            var format = new Typeface(FontFamily, FontSize, this.FontStyle, this.FontWeight);

            ProcessTextFormat();

            context.FillRectangle(GetBackgroundBrush(Terminal.CursorState.Attributes, false), new Rect(Bounds.Size));

            lock (Terminal)
            {
                int row = ViewTop;
                var verticalOffset = -row * CharacterHeight;

                var lines = Terminal.ViewPort.GetLines(ViewTop, Rows);

                foreach (var line in lines)
                {
                    if (line == null)
                    {
                        row++;
                        continue;
                    }

                    int column = 0;

                    using (context.PushPreTransform(Matrix.CreateScale(
                        line.DoubleWidth ? 2.0 : 1.0,
                        line.DoubleHeightBottom | line.DoubleHeightTop ? 2.0 : 1.0)))
                    {

                        var spanStart = 0;
                        while (column < line.Count)
                        {
                            bool selected = TextSelection == null ? false : TextSelection.Within(column, row);
                            var backgroundColor = GetBackgroundBrush(line[column].Attributes, selected) as SolidColorBrush;

                            if (column < (line.Count - 1) && GetBackgroundBrush(line[column + 1].Attributes, TextSelection == null ? false : TextSelection.Within(column + 1, row)) == backgroundColor)
                            {
                                column++;
                                continue;
                            }

                            var rect = new Rect(
                                spanStart * CharacterWidth,
                                ((row - (line.DoubleHeightBottom ? 1 : 0)) * CharacterHeight + verticalOffset) * (line.DoubleHeightBottom | line.DoubleHeightTop ? 0.5 : 1.0),
                                ((column - spanStart + 1) * CharacterWidth) + 0.9,
                                CharacterHeight + 0.9
                            );

                            context.FillRectangle(new SolidColorBrush(backgroundColor.Color, 0.6), rect);

                            column++;
                            spanStart = column;
                        }

                        row++;
                    }
                }

                row = ViewTop;
                foreach (var line in lines)
                {
                    if (line == null)
                    {
                        row++;
                        continue;
                    }

                    int column = 0;

                    using (context.PushPreTransform(Matrix.CreateScale(
                       line.DoubleWidth ? 2.0 : 1.0,
                        line.DoubleHeightBottom | line.DoubleHeightTop ? 2.0 : 1.0)))
                    {

                        var spanStart = 0;
                        string toDisplay = string.Empty;
                        while (column < line.Count)
                        {
                            bool selected = TextSelection == null ? false : TextSelection.Within(column, row);
                            var foregroundColor = GetForegroundBrush(line[column].Attributes, selected);

                            toDisplay += line[column].Char.ToString() + line[column].CombiningCharacters;
                            if (
                                column < (line.Count - 1) &&
                                GetForegroundBrush(line[column + 1].Attributes, TextSelection == null ? false : TextSelection.Within(column + 1, row)) == foregroundColor &&
                                line[column + 1].Attributes.Underscore == line[column].Attributes.Underscore &&
                                line[column + 1].Attributes.Reverse == line[column].Attributes.Reverse &&
                                line[column + 1].Attributes.Bright == line[column].Attributes.Bright &&
                                line[column + 1].Attributes.Blink == line[column].Attributes.Blink
                                )
                            {
                                column++;
                                continue;
                            }

                            var showBlink = BlinkVisible();

                            if (!line[column].Attributes.Blink || (line[column].Attributes.Blink && showBlink))
                            {
                                var rect = new Rect(
                                spanStart * CharacterWidth,
                                ((row - (line.DoubleHeightBottom ? 1 : 0)) * CharacterHeight + verticalOffset) * (line.DoubleHeightBottom | line.DoubleHeightTop ? 0.5 : 1.0),
                                ((column - spanStart + 1) * CharacterWidth) + 0.9,
                                CharacterHeight + 0.9
                            );

                                var textLayout = new FormattedText
                                {
                                    Text = toDisplay,
                                    Typeface = format
                                };

                                context.DrawText(
                                    foregroundColor,
                                    rect.TopLeft,
                                    textLayout
                                );

                                if (line[column].Attributes.Underscore)
                                {
                                    context.DrawLine(new Pen(foregroundColor), rect.BottomLeft, rect.BottomRight);
                                }
                            }

                            column++;
                            spanStart = column;
                            toDisplay = "";
                        }

                        row++;
                    }
                }

                if (Terminal.CursorState.ShowCursor)
                {
                    var cursorY = Terminal.ViewPort.TopRow - ViewTop + Terminal.CursorState.CurrentRow;
                    var cursorRect = new Rect(
                        Terminal.CursorState.CurrentColumn * CharacterWidth,
                        cursorY * CharacterHeight,
                        CharacterWidth + 0.9,
                        CharacterHeight + 0.9
                    );

                    if (IsFocused)
                    {
                        context.FillRectangle(GetForegroundBrush(Terminal.CursorState.Attributes, false), cursorRect);
                    }
                    else
                    {
                        context.DrawRectangle(new Pen(GetForegroundBrush(Terminal.CursorState.Attributes, false)), cursorRect);
                    }
                }
            }

            if (ViewDebugging)
            {
                AnnotateView(context);
            }
        }

        private void AnnotateView(DrawingContext context)
        {
            var lineNumberFormat = new Typeface(FontFamily, FontSize / 2, FontStyle, FontWeight);

            for (var i = 0; i < Rows; i++)
            {
                string s = i.ToString();
                var textLayout = new FormattedText
                {
                    Text = s.ToString(),
                    Typeface = lineNumberFormat
                };

                var y = i * CharacterHeight;
                context.DrawLine(new Pen(Brushes.Beige), new Point(0, y), new Point(Bounds.Size.Width, y));
                context.DrawText(Brushes.Yellow, new Point((Bounds.Size.Width - (CharacterWidth / 2 * s.Length)), y), textLayout);

                s = (i + 1).ToString();

                textLayout = new FormattedText { Text = s.ToString(), Typeface = lineNumberFormat };
                context.DrawText(Brushes.Green, new Point((Bounds.Size.Width - (CharacterWidth / 2 * (s.Length + 3))), y), textLayout);
            }

            var bigText = Terminal.DebugText;
            var bigTextLayout = new FormattedText { Text = bigText, Typeface = lineNumberFormat };
            context.DrawText(Brushes.Yellow, new Point((Bounds.Size.Width - bigTextLayout.Measure().Width - 100), 0), bigTextLayout);
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
                    return AttributeBrushes[(int)attribute.BackgroundColor];
                else
                    return new SolidColorBrush(Color.FromArgb(255, (byte)attribute.BackgroundRgb.Red, (byte)attribute.BackgroundRgb.Green, (byte)attribute.BackgroundRgb.Blue));
            }
            else
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
        }

        private void ProcessTextFormat()
        {
            var textLayout = new FormattedText
            {
                Text = "Q",
                Typeface = new Typeface(FontFamily, FontSize, this.FontStyle, this.FontWeight)
            };

            var size = textLayout.Measure();
            if (CharacterWidth != size.Width || CharacterHeight != size.Height)
            {
                CharacterWidth = size.Width;
                CharacterHeight = size.Height;
            }

            int columns = Convert.ToInt32(Math.Floor(Bounds.Size.Width / CharacterWidth));
            int rows = Convert.ToInt32(Math.Floor(Bounds.Size.Height / CharacterHeight));
            if (Columns != columns || Rows != rows)
            {
                Columns = columns;
                Rows = rows;
                ResizeTerminal();

                if (Connection != null)
                    Connection.SetTerminalWindowSize(columns, rows, 800, 600);
            }
        }

        private void ResizeTerminal()
        {
            Terminal.ResizeView(Columns, Rows);
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

            Task.Run(() =>
            {
                var buffer = Encoding.UTF8.GetBytes(text);
                Task.Run(() =>
                {
                    Connection.SendData(buffer);
                });
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
