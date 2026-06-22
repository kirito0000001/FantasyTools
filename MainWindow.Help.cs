using System;
using FantasyTools.Models;
using FantasyTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace FantasyTools
{
    public sealed partial class MainWindow
    {
        private void RegisterHelpKeyboardAccelerators()
        {
            var f1 = new KeyboardAccelerator
            {
                Key = VirtualKey.F1,
                ScopeOwner = RootGrid
            };
            f1.Invoked += OpenF1HelpKeyboardAccelerator_Invoked;
            RootGrid.KeyboardAccelerators.Add(f1);

            var f2 = new KeyboardAccelerator
            {
                Key = VirtualKey.F2,
                ScopeOwner = RootGrid
            };
            f2.Invoked += OpenF2HelpKeyboardAccelerator_Invoked;
            RootGrid.KeyboardAccelerators.Add(f2);
        }

        private void OpenF1HelpKeyboardAccelerator_Invoked(
            KeyboardAccelerator sender,
            KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            _ = ShowF1HelpAsync();
        }

        private void OpenF2HelpKeyboardAccelerator_Invoked(
            KeyboardAccelerator sender,
            KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            _ = ShowF2HelpAsync();
        }

        private async System.Threading.Tasks.Task ShowF1HelpAsync()
        {
            var isSettingsPage = _viewModel.SelectedModule == ToolboxModuleKey.Settings;
            var title = isSettingsPage ? "设置页 Tips 合集" : "快捷键大全";
            var content = isSettingsPage
                ? CreateHelpContent(
                    "整体设置 Tips",
                    [
                        ("夜间模式", "默认浅色；切换后立即应用并写入 settings。"),
                        ("整体项目位置", "更改位置会迁移旧整体项目目录，不能只改路径指针。迁移走全局进度条，成功后清理旧目录。"),
                        ("辅助显示", "工作区路径、当前模块说明默认关闭，只服务确认和调试。"),
                        ("Log 输出", "默认关闭；打开后使用 Unreal 风格格式：[HH:mm:ss] LogFantasyTools: Verbosity: Message。"),
                        ("快捷帮助", "F1 在设置页显示本 Tips；F2 显示当前设置页填写规则。"),
                        ("危险操作", "恢复推荐值不会删除制作数据；文件级操作不能靠 Ctrl+Z，需要底层回滚事务。")
                    ])
                : CreateHelpContent(
                    "幻杀工具箱快捷键",
                    [
                        ("F1", "普通页面打开快捷键大全；整体设置页打开设置 Tips 合集。"),
                        ("F2", "打开当前页面介绍和填写规则。"),
                        ("Enter", "在弹窗中执行默认确认；在按钮或卡片聚焦时触发当前操作。"),
                        ("Esc", "关闭当前弹窗或覆盖层；不默认退出长期编辑页面。"),
                        ("右键", "在弹窗空白处返回；后续对象卡右键菜单只放当前对象相关操作。"),
                        ("Alt + Left", "预留为次级页面返回；当前角色详情页请使用顶部返回按钮。"),
                        ("Ctrl + Z", "只用于设置页轻量设置撤回；目录迁移、图片导入、删除和同步不走这个撤回。")
                    ]);

            await _dialogService.ShowContentAsync(new ContentDialogRequest(
                title,
                content,
                PrimaryButtonText: "知道了",
                CloseButtonText: string.Empty,
                DefaultButton: ContentDialogButton.Primary));
        }

        private async System.Threading.Tasks.Task ShowF2HelpAsync()
        {
            var (title, sections) = GetCurrentPageRules();
            await _dialogService.ShowContentAsync(new ContentDialogRequest(
                title,
                CreateHelpContent(title, sections),
                PrimaryButtonText: "知道了",
                CloseButtonText: string.Empty,
                DefaultButton: ContentDialogButton.Primary));
        }

        private (string Title, (string Heading, string Body)[] Sections) GetCurrentPageRules()
        {
            if (CharacterDetailPage.Visibility == Visibility.Visible)
            {
                return ("角色编辑填写规则",
                [
                    ("基础资料", "角色英文代号是文件夹和引用命名基础，必须点“确定修改”后才会重命名目录；中文名存在时对外显示中文名。"),
                    ("卡面", "点击右侧卡面图片设置，导入时必须弹出可视化裁剪，角色卡面目标尺寸为 732x1028。"),
                    ("血量和阶段", "使用 NumberBox，只保存有效正整数；点击空白处应结束输入焦点。"),
                    ("携带技能组", "技能组可自由新增和删除；英文代号按 <角色英文代号>-Skill<x> 自动生成，用户不可手动修改。"),
                    ("Tag 和携带牌", "Tag 用于角色定位和检索；携带牌暂时以文本条目维护，后续手牌对象成型后再升级为引用。"),
                    ("保存", "普通字段短延迟自动保存；返回列表、导入卡面、修改英文代号前必须 flush 当前修改。")
                ]);
            }

            return _viewModel.SelectedModule switch
            {
                ToolboxModuleKey.Characters => ("角色页填写规则",
                [
                    ("角色卡片", "角色页用于管理武将和角色卡。点击真实卡进入角色编辑；点击 + 卡打开新建角色弹窗。"),
                    ("新建角色", "新建时必须填写角色英文代号，并选择或沿用默认卡面；弹窗底部显示创建后的角色文件夹预览。"),
                    ("显示名称", "外层卡片优先显示中文名；中文名为空时才显示英文代号。"),
                    ("文件落点", "角色数据保存在整体项目目录的 Characters/<角色英文代号>/ 下，卡面文件为 CardFace.png；旧 CardFace.jpeg / CardFace.jpg 不再兼容，会被清理。")
                ]),
                ToolboxModuleKey.HandCards => ("手牌页填写规则",
                [
                    ("手牌卡片", "手牌页用于管理基础牌、事件牌、装备牌和共鸣牌。点击真实卡进入手牌编辑；点击 + 卡打开新建手牌弹窗。"),
                    ("卡面规格", "手牌卡面目标尺寸为 357x300；导入图片时也必须走可视化裁剪和校验。"),
                    ("填写字段", "手牌资料包含名称、英文代号、介绍、花色、扑克数字、卡牌类型、函数组、剩余使用次数、武器类型、数值和数值表达式。"),
                    ("文件落点", "手牌数据保存在整体项目目录的 HandCards/手牌英文代号/ 下，卡面文件为 CardFace.png；旧 CardFace.jpeg / CardFace.jpg 不再兼容，会被清理。")
                ]),
                ToolboxModuleKey.UnrealSync => ("虚幻同步台填写规则",
                [
                    ("路径", "填写 Unreal Engine 路径和 FantasyProject 的 .uproject 路径；同步前先做路径预检查。"),
                    ("同步前置", "同步角色或手牌前必须 flush 当前工具箱数据，并按备份规范选择“备份并同步”或“直接同步”。"),
                    ("回滚", "任何会写入或覆盖文件的同步都不能只依赖 Ctrl+Z，必须建立备份或回滚事务。")
                ]),
                ToolboxModuleKey.Settings => ("整体设置填写规则",
                [
                    ("夜间模式", "选择跟随系统、浅色或深色；默认浅色，切换后立即保存。"),
                    ("整体项目位置", "选择的是父目录，工具箱会创建“幻杀工具箱项目”目录，并迁移旧目录内容。"),
                    ("辅助显示", "工作区路径和当前模块说明默认关闭，只在需要确认路径或调试时打开。"),
                    ("Log 输出", "Log 默认关闭；保存到文件也默认关闭，开启后写入整体项目目录的 Logs。"),
                    ("F1 / F2", "F1 在设置页显示设置 Tips；F2 显示本页填写规则。")
                ]),
                _ => ("当前页面填写规则尚未补齐",
                [
                    ("待补齐", "当前页面填写规则尚未补齐。")
                ])
            };
        }

        private static ScrollViewer CreateHelpContent(
            string title,
            (string Heading, string Body)[] sections)
        {
            const double contentWidth = 420;
            var panel = new StackPanel
            {
                Width = contentWidth,
                MaxWidth = contentWidth,
                Spacing = 14
            };

            panel.Children.Add(new TextBlock
            {
                Width = contentWidth,
                Text = title,
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });

            foreach (var (heading, body) in sections)
            {
                var section = new StackPanel
                {
                    Spacing = 4
                };
                section.Children.Add(new TextBlock
                {
                    Width = contentWidth,
                    Text = heading,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                });
                section.Children.Add(new TextBlock
                {
                    Width = contentWidth,
                    Text = body,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.78
                });
                panel.Children.Add(section);
            }

            return new ScrollViewer
            {
                MaxHeight = 560,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel
            };
        }
    }
}
