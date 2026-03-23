using System;
using System.Drawing;
using System.Windows.Forms;

namespace AIDictationApp.Helpers
{
    public class FloatingRecorderButtonForm : Form
    {
        private readonly Button _button;
        private Point _dragStartPoint;
        private Point _formStartPoint;
        private bool _isDragging;
        private bool _suppressClick;

        public event EventHandler? ToggleRequested;

        public FloatingRecorderButtonForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(56, 56);
            Opacity = 0.35;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;

            var initialBounds = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            Location = new Point(initialBounds.Right - Width - 24, initialBounds.Bottom - Height - 24);

            _button = new Button
            {
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Black,
                ForeColor = Color.White,
                Text = "🎤",
                Font = new Font("Segoe UI Emoji", 18f, FontStyle.Regular, GraphicsUnit.Point),
                TabStop = false,
                Cursor = Cursors.Hand,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _button.FlatAppearance.BorderSize = 0;

            _button.MouseDown += HandleMouseDown;
            _button.MouseMove += HandleMouseMove;
            _button.MouseUp += HandleMouseUp;
            _button.Click += Button_Click;

            Controls.Add(_button);
            Resize += (_, _) => ApplyCircularRegion();
            ApplyCircularRegion();
        }

        public void SetRecordingState(bool isRecording)
        {
            _button.Text = isRecording ? "⏹" : "🎤";
            _button.BackColor = isRecording ? Color.DarkRed : Color.Black;
        }

        private void ApplyCircularRegion()
        {
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddEllipse(0, 0, Width, Height);
            Region = new Region(path);
        }

        private void Button_Click(object? sender, EventArgs e)
        {
            if (_suppressClick)
            {
                _suppressClick = false;
                return;
            }

            ToggleRequested?.Invoke(this, EventArgs.Empty);
        }

        private void HandleMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            _dragStartPoint = Cursor.Position;
            _formStartPoint = Location;
            _isDragging = true;
            _suppressClick = false;
        }

        private void HandleMouseMove(object? sender, MouseEventArgs e)
        {
            if (!_isDragging)
                return;

            var current = Cursor.Position;
            int offsetX = current.X - _dragStartPoint.X;
            int offsetY = current.Y - _dragStartPoint.Y;
            Location = new Point(_formStartPoint.X + offsetX, _formStartPoint.Y + offsetY);

            if (Math.Abs(offsetX) > 3 || Math.Abs(offsetY) > 3)
            {
                _suppressClick = true;
            }
        }

        private void HandleMouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = false;
            }
        }
    }
}
