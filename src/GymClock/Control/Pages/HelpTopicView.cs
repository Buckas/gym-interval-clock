using System;
using System.Windows;
using System.Windows.Controls;
using GymClock.Link;

namespace GymClock.Wpf
{
    /// <summary>
    /// One page of the Link displays guide (LinkHelp): the title, then each line - "# " a heading,
    /// "- " a bullet, "1." a numbered step, anything else a paragraph. The same rules the old help
    /// window used.
    /// </summary>
    public sealed class HelpTopicView : StackPanel
    {
        public static readonly DependencyProperty TopicProperty = DependencyProperty.Register(
            nameof(Topic), typeof(LinkHelpTopic), typeof(HelpTopicView), new PropertyMetadata(null, (d, e) => ((HelpTopicView)d).Build()));

        public LinkHelpTopic Topic
        {
            get { return (LinkHelpTopic)GetValue(TopicProperty); }
            set { SetValue(TopicProperty, value); }
        }

        private void Build()
        {
            Children.Clear();
            LinkHelpTopic topic = Topic;
            if (topic == null) return;

            Add(topic.Title, "TextStyle.CardTitle", new Thickness(0, 0, 0, 8));
            foreach (string line in topic.Lines)
            {
                if (line.StartsWith("# ", StringComparison.Ordinal))
                {
                    Add(line.Substring(2), "TextStyle.Subtitle", new Thickness(0, 14, 0, 4));
                }
                else if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    AddMarked("•", line.Substring(2));
                }
                else if (line.Length > 2 && char.IsDigit(line[0]) && line[1] == '.')
                {
                    AddMarked(line.Substring(0, 2), line.Substring(2).TrimStart());
                }
                else
                {
                    Add(line, "TextStyle.Body", new Thickness(0, 4, 0, 2));
                }
            }
        }

        private void Add(string text, string style, Thickness margin)
        {
            TextBlock block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = margin };
            block.SetResourceReference(StyleProperty, style);
            Children.Add(block);
        }

        private void AddMarked(string mark, string text)
        {
            DockPanel row = new DockPanel { Margin = new Thickness(6, 3, 0, 2) };
            TextBlock bullet = new TextBlock { Text = mark, Width = 22 };
            bullet.SetResourceReference(TextBlock.ForegroundProperty, "Text.Muted");
            DockPanel.SetDock(bullet, Dock.Left);
            TextBlock body = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            body.SetResourceReference(StyleProperty, "TextStyle.Body");
            row.Children.Add(bullet);
            row.Children.Add(body);
            Children.Add(row);
        }
    }
}
