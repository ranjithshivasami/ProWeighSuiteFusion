using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ProWeighSuitePlatforms.Views
{
    public partial class ImageCropWindow : Window
    {
        private const double MinCropSize = 80; // minimum selection width/height in display pixels
        private const int MinCropPixels = 80; // minimum in source pixels after mapping
        private const double HandleHitRadius = 10;

        private enum ResizeHandle { None, Move, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left }

        private readonly string _sourcePath;
        private BitmapImage _sourceBitmap = null!;
        private Point _startPoint;
        private bool _dragging;
        private bool _moving;
        private Point _moveOffset;
        private ResizeHandle _activeHandle = ResizeHandle.None;
        private Rect _resizeStartRect;
        private Point _resizeStartPos;
        private Rect _selection = Rect.Empty;

        public string? CroppedImagePath { get; private set; }

        public ImageCropWindow(string sourcePath)
        {
            InitializeComponent();
            _sourcePath = sourcePath;
            Loaded += OnLoaded;
            SizeChanged += (_, __) => UpdateOverlay();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _sourceBitmap = new BitmapImage();
                _sourceBitmap.BeginInit();
                _sourceBitmap.CacheOption = BitmapCacheOption.OnLoad;
                _sourceBitmap.UriSource = new Uri(_sourcePath, UriKind.Absolute);
                _sourceBitmap.EndInit();
                _sourceBitmap.Freeze();
                SourceImage.Source = _sourceBitmap;

                // default square in center (respect minimum)
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var w = ImageContainer.ActualWidth;
                    var h = ImageContainer.ActualHeight;
                    if (w <= 0 || h <= 0) { w = 500; h = 500; }
                    double size = Math.Max(MinCropSize, Math.Min(w, h) * 0.6);
                    size = Math.Min(size, Math.Min(w, h));
                    double x = (w - size) / 2;
                    double y = (h - size) / 2;
                    _selection = new Rect(x, y, size, size);
                    UpdateOverlay();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private ResizeHandle HitTestHandle(Point p)
        {
            if (_selection.IsEmpty) return ResizeHandle.None;
            double x = _selection.X, y = _selection.Y, w = _selection.Width, h = _selection.Height;
            // center handle first
            double cx = x + w / 2, cy = y + h / 2;
            if (Math.Abs(p.X - cx) <= 16 && Math.Abs(p.Y - cy) <= 16) return ResizeHandle.Move;
            if (Math.Abs(p.X - x) <= HandleHitRadius && Math.Abs(p.Y - y) <= HandleHitRadius) return ResizeHandle.TopLeft;
            if (Math.Abs(p.X - (x + w)) <= HandleHitRadius && Math.Abs(p.Y - y) <= HandleHitRadius) return ResizeHandle.TopRight;
            if (Math.Abs(p.X - x) <= HandleHitRadius && Math.Abs(p.Y - (y + h)) <= HandleHitRadius) return ResizeHandle.BottomLeft;
            if (Math.Abs(p.X - (x + w)) <= HandleHitRadius && Math.Abs(p.Y - (y + h)) <= HandleHitRadius) return ResizeHandle.BottomRight;
            if (Math.Abs(p.X - (x + w / 2)) <= HandleHitRadius && Math.Abs(p.Y - y) <= 8) return ResizeHandle.Top;
            if (Math.Abs(p.X - (x + w / 2)) <= HandleHitRadius && Math.Abs(p.Y - (y + h)) <= 8) return ResizeHandle.Bottom;
            if (Math.Abs(p.X - x) <= 8 && Math.Abs(p.Y - (y + h / 2)) <= HandleHitRadius) return ResizeHandle.Left;
            if (Math.Abs(p.X - (x + w)) <= 8 && Math.Abs(p.Y - (y + h / 2)) <= HandleHitRadius) return ResizeHandle.Right;
            return ResizeHandle.None;
        }

        private void OverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(OverlayCanvas);
            var hit = HitTestHandle(pos);
            if (hit != ResizeHandle.None)
            {
                if (hit == ResizeHandle.Move)
                {
                    _moving = true;
                    _moveOffset = new Point(pos.X - _selection.X, pos.Y - _selection.Y);
                    OverlayCanvas.CaptureMouse();
                    return;
                }
                _activeHandle = hit;
                _resizeStartRect = _selection;
                _resizeStartPos = pos;
                OverlayCanvas.CaptureMouse();
                return;
            }
            // start new selection (free rectangular, smooth)
            _dragging = true;
            _startPoint = pos;
            _selection = new Rect(_startPoint, new Size(0, 0));
            OverlayCanvas.CaptureMouse();
            UpdateOverlay();
        }

        private void OverlayCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_moving)
            {
                var cur = e.GetPosition(OverlayCanvas);
                double newX = cur.X - _moveOffset.X;
                double newY = cur.Y - _moveOffset.Y;
                newX = Math.Max(0, Math.Min(newX, OverlayCanvas.ActualWidth - _selection.Width));
                newY = Math.Max(0, Math.Min(newY, OverlayCanvas.ActualHeight - _selection.Height));
                _selection = new Rect(newX, newY, _selection.Width, _selection.Height);
                UpdateOverlay();
                return;
            }
            if (_activeHandle != ResizeHandle.None)
            {
                var cur = e.GetPosition(OverlayCanvas);
                double dx = cur.X - _resizeStartPos.X;
                double dy = cur.Y - _resizeStartPos.Y;
                double x = _resizeStartRect.X, y = _resizeStartRect.Y, w = _resizeStartRect.Width, h = _resizeStartRect.Height;
                switch (_activeHandle)
                {
                    case ResizeHandle.TopLeft: x += dx; y += dy; w -= dx; h -= dy; break;
                    case ResizeHandle.Top: y += dy; h -= dy; break;
                    case ResizeHandle.TopRight: y += dy; w += dx; h -= dy; break;
                    case ResizeHandle.Right: w += dx; break;
                    case ResizeHandle.BottomRight: w += dx; h += dy; break;
                    case ResizeHandle.Bottom: h += dy; break;
                    case ResizeHandle.BottomLeft: x += dx; w -= dx; h += dy; break;
                    case ResizeHandle.Left: x += dx; w -= dx; break;
                }
                // enforce min
                if (w < MinCropSize) { if (_activeHandle == ResizeHandle.Left || _activeHandle == ResizeHandle.TopLeft || _activeHandle == ResizeHandle.BottomLeft) x -= (MinCropSize - w); w = MinCropSize; }
                if (h < MinCropSize) { if (_activeHandle == ResizeHandle.Top || _activeHandle == ResizeHandle.TopLeft || _activeHandle == ResizeHandle.TopRight) y -= (MinCropSize - h); h = MinCropSize; }
                // clamp to canvas
                if (x < 0) { w += x; x = 0; }
                if (y < 0) { h += y; y = 0; }
                if (x + w > OverlayCanvas.ActualWidth) w = OverlayCanvas.ActualWidth - x;
                if (y + h > OverlayCanvas.ActualHeight) h = OverlayCanvas.ActualHeight - y;
                w = Math.Max(MinCropSize, w);
                h = Math.Max(MinCropSize, h);
                _selection = new Rect(x, y, w, h);
                UpdateOverlay();
                return;
            }
            if (!_dragging) return;
            var cur2 = e.GetPosition(OverlayCanvas);
            double x2 = Math.Min(_startPoint.X, cur2.X);
            double y2 = Math.Min(_startPoint.Y, cur2.Y);
            double w2 = Math.Abs(cur2.X - _startPoint.X);
            double h2 = Math.Abs(cur2.Y - _startPoint.Y);
            // allow free rectangular (smooth), not forced square
            // clamp to canvas
            if (x2 < 0) { w2 += x2; x2 = 0; }
            if (y2 < 0) { h2 += y2; y2 = 0; }
            if (x2 + w2 > OverlayCanvas.ActualWidth) w2 = OverlayCanvas.ActualWidth - x2;
            if (y2 + h2 > OverlayCanvas.ActualHeight) h2 = OverlayCanvas.ActualHeight - y2;
            _selection = new Rect(x2, y2, w2, h2);
            UpdateOverlay();
        }

        private void OverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_moving)
            {
                _moving = false;
                OverlayCanvas.ReleaseMouseCapture();
                return;
            }
            if (_activeHandle != ResizeHandle.None)
            {
                _activeHandle = ResizeHandle.None;
                OverlayCanvas.ReleaseMouseCapture();
                // enforce min after resize
                if (_selection.Width < MinCropSize) _selection.Width = MinCropSize;
                if (_selection.Height < MinCropSize) _selection.Height = MinCropSize;
                UpdateOverlay();
                return;
            }
            _dragging = false;
            OverlayCanvas.ReleaseMouseCapture();
            // enforce minimum, if too small reset to default center with at least MinCropSize
            if (_selection.Width < MinCropSize || _selection.Height < MinCropSize)
            {
                var w = OverlayCanvas.ActualWidth;
                var h = OverlayCanvas.ActualHeight;
                double size = Math.Max(MinCropSize, Math.Min(w, h) * 0.6);
                size = Math.Min(size, Math.Min(w, h));
                _selection = new Rect((w - size) / 2, (h - size) / 2, size, size);
                UpdateOverlay();
            }
        }

        private void UpdateOverlay()
        {
            if (_selection.IsEmpty || _selection.Width <= 0)
            {
                SelectionRect.Visibility = Visibility.Collapsed;
                DimRect.Visibility = Visibility.Collapsed;
                HandleTL.Visibility = Visibility.Collapsed;
                HandleTR.Visibility = Visibility.Collapsed;
                HandleBL.Visibility = Visibility.Collapsed;
                HandleBR.Visibility = Visibility.Collapsed;
                HandleT.Visibility = Visibility.Collapsed;
                HandleB.Visibility = Visibility.Collapsed;
                HandleL.Visibility = Visibility.Collapsed;
                HandleR.Visibility = Visibility.Collapsed;
                CenterHandle.Visibility = Visibility.Collapsed;
                return;
            }
            Canvas.SetLeft(SelectionRect, _selection.X);
            Canvas.SetTop(SelectionRect, _selection.Y);
            SelectionRect.Width = _selection.Width;
            SelectionRect.Height = _selection.Height;
            SelectionRect.Visibility = Visibility.Visible;

            Canvas.SetLeft(HandleTL, _selection.X - 6);
            Canvas.SetTop(HandleTL, _selection.Y - 6);
            HandleTL.Visibility = Visibility.Visible;

            Canvas.SetLeft(HandleTR, _selection.X + _selection.Width - 6);
            Canvas.SetTop(HandleTR, _selection.Y - 6);
            HandleTR.Visibility = Visibility.Visible;

            Canvas.SetLeft(HandleBL, _selection.X - 6);
            Canvas.SetTop(HandleBL, _selection.Y + _selection.Height - 6);
            HandleBL.Visibility = Visibility.Visible;

            Canvas.SetLeft(HandleBR, _selection.X + _selection.Width - 6);
            Canvas.SetTop(HandleBR, _selection.Y + _selection.Height - 6);
            HandleBR.Visibility = Visibility.Visible;

            Canvas.SetLeft(HandleT, _selection.X + _selection.Width / 2 - 7);
            Canvas.SetTop(HandleT, _selection.Y - 4);
            HandleT.Visibility = Visibility.Visible;

            Canvas.SetLeft(HandleB, _selection.X + _selection.Width / 2 - 7);
            Canvas.SetTop(HandleB, _selection.Y + _selection.Height - 4);
            HandleB.Visibility = Visibility.Visible;

            Canvas.SetLeft(HandleL, _selection.X - 4);
            Canvas.SetTop(HandleL, _selection.Y + _selection.Height / 2 - 7);
            HandleL.Visibility = Visibility.Visible;

            Canvas.SetLeft(HandleR, _selection.X + _selection.Width - 4);
            Canvas.SetTop(HandleR, _selection.Y + _selection.Height / 2 - 7);
            HandleR.Visibility = Visibility.Visible;

            Canvas.SetLeft(CenterHandle, _selection.X + _selection.Width / 2 - CenterHandle.Width / 2);
            Canvas.SetTop(CenterHandle, _selection.Y + _selection.Height / 2 - CenterHandle.Height / 2);
            CenterHandle.Visibility = Visibility.Visible;

            DimRect.Width = OverlayCanvas.ActualWidth;
            DimRect.Height = OverlayCanvas.ActualHeight;
            DimRect.Visibility = Visibility.Visible;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_sourceBitmap == null || _selection.IsEmpty || _selection.Width < MinCropSize || _selection.Height < MinCropSize)
                {
                    MessageBox.Show($"Please select a crop area of at least {MinCropSize}×{MinCropSize}.", "Crop", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Map selection from displayed coordinates to source pixel coordinates
                var cropped = CreateCroppedBitmap();
                if (cropped == null)
                {
                    MessageBox.Show("Failed to crop. Try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProWeighSuiteFusion", "MaterialImages");
                Directory.CreateDirectory(dir);
                var fileName = $"mat_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.jpg";
                var outPath = Path.Combine(dir, fileName);

                var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
                encoder.Frames.Add(BitmapFrame.Create(cropped));
                using (var fs = new FileStream(outPath, FileMode.Create))
                    encoder.Save(fs);

                CroppedImagePath = outPath;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Crop failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private BitmapSource? CreateCroppedBitmap()
        {
            // Determine displayed image bounds inside OverlayCanvas (Uniform stretch)
            double canvasW = OverlayCanvas.ActualWidth;
            double canvasH = OverlayCanvas.ActualHeight;
            if (canvasW <= 0 || canvasH <= 0) return null;

            int srcW = _sourceBitmap.PixelWidth;
            int srcH = _sourceBitmap.PixelHeight;

            double scale;
            double dispW, dispH, offsetX, offsetY;
            double imgAspect = (double)srcW / srcH;
            double canvasAspect = canvasW / canvasH;
            if (imgAspect > canvasAspect)
            {
                // fit width
                dispW = canvasW;
                dispH = canvasW / imgAspect;
                scale = (double)srcW / dispW;
                offsetX = 0;
                offsetY = (canvasH - dispH) / 2;
            }
            else
            {
                dispH = canvasH;
                dispW = canvasH * imgAspect;
                scale = (double)srcH / dispH;
                offsetX = (canvasW - dispW) / 2;
                offsetY = 0;
            }

            // selection relative to displayed image
            double selX = _selection.X - offsetX;
            double selY = _selection.Y - offsetY;
            double selW = _selection.Width;
            double selH = _selection.Height;

            // clamp to displayed image bounds
            if (selX < 0) { selW += selX; selX = 0; }
            if (selY < 0) { selH += selY; selY = 0; }
            if (selX + selW > dispW) selW = dispW - selX;
            if (selY + selH > dispH) selH = dispH - selY;
            if (selW <= 0 || selH <= 0) return null;

            int px = (int)Math.Round(selX * scale);
            int py = (int)Math.Round(selY * scale);
            int pw = (int)Math.Round(selW * scale);
            int ph = (int)Math.Round(selH * scale);
            // clamp to source
            px = Math.Max(0, Math.Min(px, srcW - 1));
            py = Math.Max(0, Math.Min(py, srcH - 1));
            if (px + pw > srcW) pw = srcW - px;
            if (py + ph > srcH) ph = srcH - py;
            if (pw < MinCropPixels || ph < MinCropPixels) return null;

            return new CroppedBitmap(_sourceBitmap, new Int32Rect(px, py, pw, ph));
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
