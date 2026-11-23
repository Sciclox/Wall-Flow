using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WallFlow.Windows;

/// <summary>
/// Attached property to render basic Markdown formatting (bold, italics, inline code, code blocks, lists) inside WPF TextBlocks.
/// </summary>
public static class MarkdownHelper
{
    public static readonly DependencyProperty FormattedTextProperty =
        DependencyProperty.RegisterAttached(
            "FormattedText",
            typeof(string),
            typeof(MarkdownHelper),
            new PropertyMetadata(string.Empty, OnFormattedTextChanged));

    public static string GetFormattedText(DependencyObject obj)
    {
        return (string)obj.GetValue(FormattedTextProperty);
    }

    public static void SetFormattedText(DependencyObject obj, string value)
    {
        obj.SetValue(FormattedTextProperty, value);
    }

    private static void OnFormattedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock textBlock)
        {
            textBlock.Inlines.Clear();
            string text = e.NewValue as string ?? string.Empty;
            if (string.IsNullOrEmpty(text)) return;

            ParseMarkdownToInlines(text, textBlock.Inlines, textBlock.FontSize);
        }
    }

    public static void ParseMarkdownToInlines(string text, InlineCollection inlines, double baseFontSize)
    {
        string[] lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        bool inCodeBlock = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            if (line.TrimStart().StartsWith("```"))
            {
                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (i > 0)
            {
                inlines.Add(new LineBreak());
            }

            if (inCodeBlock)
            {
                var codeSpan = new Span(new Run(line))
                {
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 203, 107)),
                    Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255))
                };
                inlines.Add(codeSpan);
                continue;
            }

            if (line.StartsWith("# "))
            {
                AddFormattedText(line.Substring(2), inlines, isBold: true, fontSize: baseFontSize + 4);
            }
            else if (line.StartsWith("## "))
            {
                AddFormattedText(line.Substring(3), inlines, isBold: true, fontSize: baseFontSize + 3);
            }
            else if (line.StartsWith("### "))
            {
                AddFormattedText(line.Substring(4), inlines, isBold: true, fontSize: baseFontSize + 2);
            }
            else if (line.StartsWith("* ") || line.StartsWith("- ") || line.StartsWith("• "))
            {
                inlines.Add(new Run("• "));
                string content = line.Length > 2 ? line.Substring(2) : "";
                AddFormattedText(content, inlines, isBold: false, fontSize: baseFontSize);
            }
            else
            {
                AddFormattedText(line, inlines, isBold: false, fontSize: baseFontSize);
            }
        }
    }

    private static void AddFormattedText(string text, InlineCollection inlines, bool isBold, double fontSize)
    {
        var regex = new Regex(@"(\*\*.*?\*\*|__.*?__|`.*?`|\*.*?\*|_.*?_)");
        var parts = regex.Split(text);

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;

            if ((part.StartsWith("**") && part.EndsWith("**") && part.Length >= 4) ||
                (part.StartsWith("__") && part.EndsWith("__") && part.Length >= 4))
            {
                string inner = part.Substring(2, part.Length - 4);
                var boldRun = new Run(inner) { FontWeight = FontWeights.Bold, FontSize = fontSize };
                inlines.Add(boldRun);
            }
            else if ((part.StartsWith("*") && part.EndsWith("*") && part.Length >= 2) ||
                     (part.StartsWith("_") && part.EndsWith("_") && part.Length >= 2))
            {
                string inner = part.Substring(1, part.Length - 2);
                var italicRun = new Run(inner) { FontStyle = FontStyles.Italic, FontSize = fontSize };
                if (isBold) italicRun.FontWeight = FontWeights.Bold;
                inlines.Add(italicRun);
            }
            else if (part.StartsWith("`") && part.EndsWith("`") && part.Length >= 2)
            {
                string inner = part.Substring(1, part.Length - 2);
                var codeRun = new Run(" " + inner + " ")
                {
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 203, 107)),
                    Background = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
                    FontSize = fontSize - 1
                };
                inlines.Add(codeRun);
            }
            else
            {
                var normalRun = new Run(part) { FontSize = fontSize };
                if (isBold) normalRun.FontWeight = FontWeights.Bold;
                inlines.Add(normalRun);
            }
        }
    }
}
