using System;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Dispatching;



namespace auto_cliker;

using System.Reflection.Metadata;

#if WINDOWS
using System.Runtime.InteropServices;
#elif MACCATALYST
using CoreGraphics;
using AppKit;
using UIKit;
using ObjCRuntime;
using Foundation;
using System.Runtime.InteropServices;
#endif

public partial class MainPage : ContentPage
{
#if MACCATALYST
    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    static extern void CGEventPost(int tap, IntPtr @event);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    static extern IntPtr CGEventCreateMouseEvent(IntPtr source, CGEventType mouseType, CGPoint mouseCursorPosition, CGMouseButton mouseButton);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    static extern CGPoint CGEventGetLocation(IntPtr @event);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    static extern IntPtr CGEventCreate(IntPtr source);

    enum CGEventType
    {
        LeftMouseDown = 1,
        LeftMouseUp = 2
    }

    enum CGMouseButton
    {
        Left = 0
    }

    const int kCGHIDEventTap = 0;
#endif

#if WINDOWS
    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    static extern void mouse_event(MouseEventFlags dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT lpPoint);

    private struct POINT
    {
        public int X;
        public int Y;
    }
    [Flags]
    enum MouseEventFlags : uint
    {
        LEFTDOWN = 0x0002,
        LEFTUP = 0x0004
    }
#endif

    private CancellationTokenSource _cts;

    private IDispatcherTimer _timer;
    private int _currentClick;
    private int _repeatCount;
    private int _clickSpeed;
    private (int X, int Y) _point1;
    private (int X, int Y) _point2;
    private string _order;

    public MainPage()
    {
        InitializeComponent();

        // Set up text changed handlers for numeric-only input
        ClickSpeedEntry.TextChanged += NumericOnlyHandler;
        RepeatCountEntry.TextChanged += NumericOnlyHandler;
        Point1XEntry.TextChanged += NumericOnlyHandler;
        Point1YEntry.TextChanged += NumericOnlyHandler;
        Point2XEntry.TextChanged += NumericOnlyHandler;
        Point2YEntry.TextChanged += NumericOnlyHandler;
    }

    private void NumericOnlyHandler(object sender, TextChangedEventArgs e)
    {
        var entry = (Entry)sender;
        string valid = new string(entry.Text?.Where(c => char.IsDigit(c) || c == '.').ToArray());

        if (entry.Text != valid)
            entry.Text = valid;
    }



    private Task<Point> GetMouseClickPointAsync()
    {
        var tcs = new TaskCompletionSource<Point>();

#if WINDOWS
        Task.Run(() =>
        {
            while (true)
            {
                if (GetAsyncKeyState(0x01) < 0)
                {
                    GetCursorPos(out POINT p);
                    tcs.SetResult(new Point(p.X, p.Y));
                    break;
                }
            }
        });
#elif MACCATALYST
    
    IntPtr cgEvent = CGEventCreate(IntPtr.Zero);
    CGPoint point = CGEventGetLocation(cgEvent);

    int x = (int)point.X;
    int y = (int)point.Y;
    tcs.SetResult(new Point(x,y));
#else
    tcs.SetResult(new Point(0, 0)); // Default fallback
#endif

        return tcs.Task;
    }



    private async void OnAddPoint1ByMouseClicked(object sender, EventArgs e)
    {
        var point = await GetMouseClickPointAsync();
        Point1XEntry.Text = point.X.ToString();
        Point1YEntry.Text = point.Y.ToString();
    }

    private async void OnAddPoint2ByMouseClicked(object sender, EventArgs e)
    {
        var point = await GetMouseClickPointAsync();
        Point2XEntry.Text = point.X.ToString();
        Point2YEntry.Text = point.Y.ToString();
    }

    private void OnStopClicked(object sender, EventArgs e)
    {
        _cts?.Cancel();
    }


    private async void OnStartClick(object sender, EventArgs e)
    {
        
        try
        {
            _cts = new CancellationTokenSource();
            _clickSpeed = int.Parse(ClickSpeedEntry.Text);
            _repeatCount = int.Parse(RepeatCountEntry.Text);
            _point1 = (int.Parse(Point1XEntry.Text), int.Parse(Point1YEntry.Text));
            _point2 = (int.Parse(Point2XEntry.Text), int.Parse(Point2YEntry.Text));
            _order = OrderPicker.SelectedItem?.ToString() ?? "Point 1 → Point 2";

            _currentClick = 0;

            _timer = Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(_clickSpeed);
            _timer.Tick += async (s, args) =>
            {
                if (_cts.Token.IsCancellationRequested || _currentClick >= _repeatCount)
                {
                    _timer.Stop();
                    return;
                }

                await Task.Run(() =>
                {
                    PerformClickByOrderAsync();
                });

                _currentClick++;

                await Task.Delay(200); 

            };
            _timer.Start();

        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"Invalid input: {ex.Message}", "OK");
        }
    }

    private async Task PerformClickByOrderAsync()
    {
        await Task.Run(() =>
        {
            if (_order == "Point 1 → Point 2")
            {
                ClickAt(_point1.X, _point1.Y);
                ClickAt(_point2.X, _point2.Y);
            }
            else if (_order == "Point 2 → Point 1")
            {
                ClickAt(_point2.X, _point2.Y);
                ClickAt(_point1.X, _point1.Y);
            }
            else // Alternate
            {
                if (_currentClick % 2 == 0)
                    ClickAt(_point1.X, _point1.Y);
                else
                    ClickAt(_point2.X, _point2.Y);
            }
        });
    }


    private void ClickAt(int x, int y)
    { 
#if WINDOWS
        SetCursorPos(x, y);
        mouse_event(MouseEventFlags.LEFTDOWN | MouseEventFlags.LEFTUP, x, y, 0, 0);
#elif MACCATALYST
        CGPoint point = new CGPoint(x, y);

        // Create mouse down event
        IntPtr mouseDown = CGEventCreateMouseEvent(IntPtr.Zero, CGEventType.LeftMouseDown, point, CGMouseButton.Left);
        CGEventPost(kCGHIDEventTap, mouseDown);

        // Create mouse up event
        IntPtr mouseUp = CGEventCreateMouseEvent(IntPtr.Zero, CGEventType.LeftMouseUp, point, CGMouseButton.Left);
        CGEventPost(kCGHIDEventTap, mouseUp);
#endif
    }
}
