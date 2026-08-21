using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace LocalLlmConsole;

/// <summary>
/// Лёгкий рендер markdown-заметок релиза в TextBlock (Inlines).
/// Поддерживается: заголовки (# / ##), жирный (**...**), списки (- / *), переносы.
/// Собственный класс форка — вендорский код не трогаем.
/// </summary>
internal static class MarkdownTextBlockBuilder
{
    public static TextBlock Build(string markdown)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0)
        };

        if (string.IsNullOrWhiteSpace(markdown))
            return textBlock;

        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                textBlock.Inlines.Add(new LineBreak());
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
                AddRun(textBlock, line[4..], FontWeights.SemiBold, 13.5);
            else if (line.StartsWith("## ", StringComparison.Ordinal))
                AddRun(textBlock, line[3..], FontWeights.SemiBold, 14);
            else if (line.StartsWith("# ", StringComparison.Ordinal))
                AddRun(textBlock, line[2..], FontWeights.Bold, 15);
            else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
                AddInlineRuns(textBlock, "• " + line[2..]);
            else
                AddInlineRuns(textBlock, line);

            textBlock.Inlines.Add(new LineBreak());
        }

        return textBlock;
    }

    private static void AddRun(TextBlock textBlock, string text, FontWeight weight, double fontSize)
    {
        textBlock.Inlines.Add(new Run(text) { FontWeight = weight, FontSize = fontSize });
    }

    /// <summary>
    /// Разбирает **жирный** текст в последовательность Run / Bold Run.
    /// </summary>
    private static void AddInlineRuns(TextBlock textBlock, string text)
    {
        var parts = text.Split("**");
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0)
                continue;

            var run = new Run(parts[i]);
            if (i % 2 == 1)
                run.FontWeight = FontWeights.Bold;
            textBlock.Inlines.Add(run);
        }
    }
}
