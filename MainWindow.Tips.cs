using System;
using System.Collections.Generic;
using FantasyTools.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace FantasyTools
{
    public sealed partial class MainWindow
    {
        private const int MaxFloatingTipCount = 4;
        private readonly Dictionary<InfoBar, DispatcherQueueTimer> _floatingTipTimers = [];

        private void ShowFloatingTip(InfoBarSeverity severity, string title, string message, string? logText = null)
        {
            severity = NormalizeFloatingTipSeverity(severity);
            var textForLog = string.IsNullOrWhiteSpace(logText)
                ? $"{title}：{message}"
                : logText;
            var verbosity = severity switch
            {
                InfoBarSeverity.Error => LogVerbosity.Error,
                InfoBarSeverity.Warning => LogVerbosity.Warning,
                _ => LogVerbosity.Display
            };
            _viewModel.Settings.AppendLog(verbosity, textForLog);

            if (severity == InfoBarSeverity.Error)
            {
                CopyTextToClipboard(textForLog);
                message = string.IsNullOrWhiteSpace(message)
                    ? "错误详情已复制到剪贴板，并写入 Log。"
                    : $"{message} 错误详情已复制到剪贴板，并写入 Log。";
            }

            var tip = new InfoBar
            {
                Severity = severity,
                Title = title,
                Message = message,
                IsOpen = true,
                IsClosable = true,
                RenderTransform = new TranslateTransform { Y = -18 },
                Opacity = 0
            };
            tip.CloseButtonClick += (_, _) => RemoveFloatingTip(tip);

            TrimFloatingTipsBeforeAdd(severity);
            FloatingTipsPanel.Children.Add(tip);
            PlayFloatingTipEntrance(tip);

            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = GetFloatingTipDuration(severity);
            timer.Tick += (_, _) => RemoveFloatingTip(tip);
            _floatingTipTimers[tip] = timer;
            timer.Start();
        }

        private void TrimFloatingTipsBeforeAdd(InfoBarSeverity incomingSeverity)
        {
            while (FloatingTipsPanel.Children.Count >= MaxFloatingTipCount)
            {
                InfoBar? target = null;
                foreach (var child in FloatingTipsPanel.Children)
                {
                    if (child is InfoBar tip && tip.Severity != InfoBarSeverity.Error)
                    {
                        target = tip;
                        break;
                    }
                }

                target ??= FloatingTipsPanel.Children[0] as InfoBar;
                if (target is null)
                {
                    return;
                }

                RemoveFloatingTip(target);
            }
        }

        private void RemoveFloatingTip(InfoBar tip)
        {
            if (_floatingTipTimers.Remove(tip, out var timer))
            {
                timer.Stop();
            }

            FloatingTipsPanel.Children.Remove(tip);
        }

        private void PlayFloatingTipEntrance(InfoBar tip)
        {
            var steps = 0;
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(16);
            timer.Tick += (_, _) =>
            {
                steps++;
                var progress = Math.Min(1, steps / 12d);
                var eased = 1 - Math.Pow(1 - progress, 3);
                tip.Opacity = eased;
                if (tip.RenderTransform is TranslateTransform transform)
                {
                    transform.Y = -18 + 18 * eased;
                }

                if (progress >= 1)
                {
                    timer.Stop();
                }
            };
            timer.Start();
        }

        private static TimeSpan GetFloatingTipDuration(InfoBarSeverity severity)
        {
            severity = NormalizeFloatingTipSeverity(severity);
            return severity switch
            {
                InfoBarSeverity.Error => TimeSpan.FromSeconds(5),
                InfoBarSeverity.Warning => TimeSpan.FromSeconds(3.2),
                _ => TimeSpan.FromSeconds(2.6)
            };
        }

        private static InfoBarSeverity NormalizeFloatingTipSeverity(InfoBarSeverity severity)
        {
            return severity == InfoBarSeverity.Informational
                ? InfoBarSeverity.Success
                : severity;
        }

        private static void CopyTextToClipboard(string text)
        {
            try
            {
                var package = new DataPackage();
                package.SetText(text);
                Clipboard.SetContent(package);
            }
            catch
            {
                // Clipboard failures should not create another user-facing failure loop.
            }
        }
    }
}
