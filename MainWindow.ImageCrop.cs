using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using FantasyTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace FantasyTools
{
    public sealed partial class MainWindow
    {
        private TaskCompletionSource<Rectangle?>? _imageCropCompletion;
        private int _imageCropSourceWidth;
        private int _imageCropSourceHeight;
        private int _imageCropTargetWidth;
        private int _imageCropTargetHeight;
        private double _imageCropScale = 1;
        private double _imageCropOffsetX;
        private double _imageCropOffsetY;
        private bool _isPanningImageCrop;
        private Windows.Foundation.Point _lastImageCropPointerPosition;

        private async Task<Rectangle?> ShowImageCropDialogAsync(
            string title,
            string sourcePath,
            int targetWidth,
            int targetHeight)
        {
            var (width, height) = CharacterWorkspaceService.GetImageSize(sourcePath);
            _imageCropCompletion = new TaskCompletionSource<Rectangle?>();
            _imageCropSourceWidth = width;
            _imageCropSourceHeight = height;
            _imageCropTargetWidth = targetWidth;
            _imageCropTargetHeight = targetHeight;

            ImageCropTitleText.Text = title;
            ImageCropSubtitleText.Text = $"源图 {width}x{height}，目标 {targetWidth}x{targetHeight}。滚轮缩放，拖动平移，双击重置，右键或 Esc 取消。";
            ImageCropImage.Source = await LoadBitmapFromFileAsync(sourcePath);
            ResetImageCropControls();
            ImageCropHost.Width = RootGrid.ActualWidth;
            ImageCropHost.Height = RootGrid.ActualHeight;
            ImageCropPopup.XamlRoot = RootGrid.XamlRoot;
            ImageCropPopup.IsOpen = true;
            ImageCropHost.Visibility = Visibility.Visible;
            ImageCropHost.Focus(FocusState.Programmatic);
            return await _imageCropCompletion.Task;
        }

        private static async Task<BitmapImage> LoadBitmapFromFileAsync(string filePath)
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            await using var fileStream = await file.OpenStreamForReadAsync();
            using var memoryStream = new InMemoryRandomAccessStream();
            await fileStream.CopyToAsync(memoryStream.AsStreamForWrite());
            memoryStream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(memoryStream);
            return bitmap;
        }

        private void ResetImageCropControls()
        {
            _imageCropScale = 1;
            _imageCropOffsetX = 0;
            _imageCropOffsetY = 0;
            _isPanningImageCrop = false;
            UpdateImageCropPreview();
        }

        private void UpdateImageCropPreview()
        {
            var sourceRatio = _imageCropSourceWidth / (double)Math.Max(1, _imageCropSourceHeight);
            const double maxPreview = 460;
            if (sourceRatio >= 1)
            {
                ImageCropPreviewFrame.Width = maxPreview;
                ImageCropPreviewFrame.Height = maxPreview / sourceRatio;
            }
            else
            {
                ImageCropPreviewFrame.Height = maxPreview;
                ImageCropPreviewFrame.Width = maxPreview * sourceRatio;
            }

            ImageCropPreviewFrame.Clip = new RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, ImageCropPreviewFrame.Width, ImageCropPreviewFrame.Height)
            };
            UpdateImageCropTargetFrameSize();
            var imageSize = GetImageCropImagePreviewSize();
            ImageCropImage.Width = imageSize.Width;
            ImageCropImage.Height = imageSize.Height;
            ClampImageCropPan();
            ImageCropImageTransform.ScaleX = _imageCropScale;
            ImageCropImageTransform.ScaleY = _imageCropScale;
            ImageCropImageTransform.TranslateX = _imageCropOffsetX;
            ImageCropImageTransform.TranslateY = _imageCropOffsetY;
        }

        private void UpdateImageCropTargetFrameSize()
        {
            var targetRatio = _imageCropTargetWidth / (double)Math.Max(1, _imageCropTargetHeight);
            var frameWidth = Math.Max(1, ImageCropPreviewFrame.Width);
            var frameHeight = Math.Max(1, ImageCropPreviewFrame.Height);
            if (frameWidth / frameHeight >= targetRatio)
            {
                ImageCropTargetFrame.Height = frameHeight;
                ImageCropTargetFrame.Width = frameHeight * targetRatio;
            }
            else
            {
                ImageCropTargetFrame.Width = frameWidth;
                ImageCropTargetFrame.Height = frameWidth / targetRatio;
            }

            UpdateImageCropMasks(frameWidth, frameHeight);
        }

        private void UpdateImageCropMasks(double frameWidth, double frameHeight)
        {
            var targetWidth = Math.Max(0, ImageCropTargetFrame.Width);
            var targetHeight = Math.Max(0, ImageCropTargetFrame.Height);
            var horizontalGap = Math.Max(0, (frameWidth - targetWidth) / 2);
            var verticalGap = Math.Max(0, (frameHeight - targetHeight) / 2);

            ImageCropMaskTop.Height = verticalGap;
            ImageCropMaskBottom.Height = verticalGap;
            ImageCropMaskLeft.Width = horizontalGap;
            ImageCropMaskLeft.Height = targetHeight;
            ImageCropMaskRight.Width = horizontalGap;
            ImageCropMaskRight.Height = targetHeight;
        }

        private void ClampImageCropPan()
        {
            var imageSize = GetImageCropImagePreviewSize();
            var maxX = Math.Max(0, (imageSize.Width * _imageCropScale - ImageCropTargetFrame.Width) / 2);
            var maxY = Math.Max(0, (imageSize.Height * _imageCropScale - ImageCropTargetFrame.Height) / 2);
            _imageCropOffsetX = Math.Clamp(_imageCropOffsetX, -maxX, maxX);
            _imageCropOffsetY = Math.Clamp(_imageCropOffsetY, -maxY, maxY);
        }

        private (double Width, double Height) GetImageCropImagePreviewSize()
        {
            var frameWidth = Math.Max(1, ImageCropPreviewFrame.Width);
            var frameHeight = Math.Max(1, ImageCropPreviewFrame.Height);
            if (_imageCropSourceWidth <= 0 || _imageCropSourceHeight <= 0)
            {
                return (frameWidth, frameHeight);
            }

            var sourceRatio = _imageCropSourceWidth / (double)_imageCropSourceHeight;
            var frameRatio = frameWidth / frameHeight;
            return sourceRatio >= frameRatio
                ? (frameHeight * sourceRatio, frameHeight)
                : (frameWidth, frameWidth / sourceRatio);
        }

        private Rectangle BuildImageCropRectangle()
        {
            var scale = Math.Max(1, _imageCropScale);
            var imageSize = GetImageCropImagePreviewSize();
            var scaledImageWidth = imageSize.Width * scale;
            var scaledImageHeight = imageSize.Height * scale;
            var imageLeft = (ImageCropPreviewFrame.Width - scaledImageWidth) / 2 + _imageCropOffsetX;
            var imageTop = (ImageCropPreviewFrame.Height - scaledImageHeight) / 2 + _imageCropOffsetY;
            var targetLeft = (ImageCropPreviewFrame.Width - ImageCropTargetFrame.Width) / 2;
            var targetTop = (ImageCropPreviewFrame.Height - ImageCropTargetFrame.Height) / 2;

            var cropX = (targetLeft - imageLeft) / scaledImageWidth * _imageCropSourceWidth;
            var cropY = (targetTop - imageTop) / scaledImageHeight * _imageCropSourceHeight;
            var cropWidth = ImageCropTargetFrame.Width / scaledImageWidth * _imageCropSourceWidth;
            var cropHeight = ImageCropTargetFrame.Height / scaledImageHeight * _imageCropSourceHeight;
            cropWidth = Math.Min(cropWidth, _imageCropSourceWidth);
            cropHeight = Math.Min(cropHeight, _imageCropSourceHeight);

            var maxX = Math.Max(0, _imageCropSourceWidth - cropWidth);
            var maxY = Math.Max(0, _imageCropSourceHeight - cropHeight);
            var x = (int)Math.Round(Math.Clamp(cropX, 0, maxX));
            var y = (int)Math.Round(Math.Clamp(cropY, 0, maxY));
            var width = Math.Clamp((int)Math.Round(cropWidth), 1, _imageCropSourceWidth - x);
            var height = Math.Clamp((int)Math.Round(cropHeight), 1, _imageCropSourceHeight - y);
            return new Rectangle(x, y, width, height);
        }

        private void CompleteImageCrop(Rectangle? crop)
        {
            var completion = _imageCropCompletion;
            _imageCropCompletion = null;
            ImageCropImage.Source = null;
            ImageCropHost.Visibility = Visibility.Collapsed;
            ImageCropPopup.IsOpen = false;
            completion?.TrySetResult(crop);
        }

        private void ImageCropConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            CompleteImageCrop(BuildImageCropRectangle());
        }

        private void ImageCropCancelButton_Click(object sender, RoutedEventArgs e)
        {
            CompleteImageCrop(null);
        }

        private void ImageCropHost_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            CompleteImageCrop(null);
            e.Handled = true;
        }

        private void ImageCropPreviewFrame_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            CompleteImageCrop(null);
            e.Handled = true;
        }

        private void ImageCropPreviewFrame_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            ResetImageCropControls();
            e.Handled = true;
        }

        private void ImageCropPreviewFrame_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(ImageCropPreviewFrame);
            var factor = point.Properties.MouseWheelDelta > 0 ? 1.08 : 1 / 1.08;
            _imageCropScale = Math.Clamp(_imageCropScale * factor, 1, 6);
            UpdateImageCropPreview();
            e.Handled = true;
        }

        private void ImageCropPreviewFrame_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(ImageCropPreviewFrame);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            _isPanningImageCrop = true;
            _lastImageCropPointerPosition = point.Position;
            ImageCropPreviewFrame.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void ImageCropPreviewFrame_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isPanningImageCrop)
            {
                return;
            }

            var point = e.GetCurrentPoint(ImageCropPreviewFrame);
            if (!point.Properties.IsLeftButtonPressed)
            {
                EndImageCropPan(e);
                return;
            }

            var position = point.Position;
            _imageCropOffsetX += position.X - _lastImageCropPointerPosition.X;
            _imageCropOffsetY += position.Y - _lastImageCropPointerPosition.Y;
            _lastImageCropPointerPosition = position;
            UpdateImageCropPreview();
            e.Handled = true;
        }

        private void ImageCropPreviewFrame_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            EndImageCropPan(e);
        }

        private void ImageCropPreviewFrame_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            EndImageCropPan(e);
        }

        private void ImageCropPreviewFrame_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            EndImageCropPan(e);
        }

        private void EndImageCropPan(PointerRoutedEventArgs e)
        {
            if (!_isPanningImageCrop)
            {
                return;
            }

            _isPanningImageCrop = false;
            ImageCropPreviewFrame.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }

        private void ImageCropHost_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                CompleteImageCrop(BuildImageCropRectangle());
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                CompleteImageCrop(null);
                e.Handled = true;
            }
        }
    }
}
