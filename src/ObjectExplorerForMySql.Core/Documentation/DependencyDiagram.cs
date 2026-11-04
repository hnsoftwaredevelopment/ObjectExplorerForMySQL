using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ObjectExplorerForMySql.Core.Dependencies;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ObjectExplorerForMySql.Core.Documentation;

/// <summary>A drawn diagram: a PDF of one page with the size of the drawing, in points.</summary>
/// <param name="Pdf">The PDF file.</param>
/// <param name="Width">The width in points.</param>
/// <param name="Height">The height in points.</param>
internal sealed record DiagramImage(byte[] Pdf, double Width, double Height);

/// <summary>
/// Draws the dependencies of one object as the Mermaid <c>flowchart LR</c> of the Markdown version does (US-1202):
/// rounded boxes in the colors of the default Mermaid theme, what uses the object on the left, the object in the
/// middle with a thick border, what it uses on the right. Arrows point from user to used; a foreign key carries
/// its name and a text match is dashed. Drawn with PDFsharp, so no browser or online service is needed.
/// </summary>
internal static class DependencyDiagram
{
    private const double Margin = 4;
    private const double PadX = 10;
    private const double PadY = 6;
    private const double RowGap = 10;
    private const double MinEdge = 56;
    private const double Arrow = 7;
    private const double LabelOffset = 8;

    private static readonly XColor Fill = XColor.FromArgb(0xEC, 0xEC, 0xFF);
    private static readonly XColor Stroke = XColor.FromArgb(0x93, 0x70, 0xDB);
    private static readonly XColor Ink = XColor.FromArgb(0x33, 0x33, 0x33);
    private static readonly XColor LabelFill = XColor.FromArgb(0xE8, 0xE8, 0xE8);

    /// <summary>Draws the diagram of <paramref name="result"/>; names in other databases get their database.</summary>
    public static DiagramImage Render(DependencyResult result, string database)
    {
        PdfDocumentWriter.EnsureFonts();
        var nameFont = new XFont(PdfDocumentWriter.FontName, 9);
        var kindFont = new XFont(PdfDocumentWriter.FontName, 7.5);
        var labelFont = new XFont(PdfDocumentWriter.FontName, 7.5);
        var measure = XGraphics.CreateMeasureContext(new XSize(4000, 4000), XGraphicsUnit.Point, XPageDirection.Downwards);

        Box MakeBox(SchemaObject target)
        {
            var name = MarkdownDocumentWriter.Label(target, database);
            var kind = MarkdownDocumentWriter.KindText(target.Kind);
            var width = Math.Max(measure.MeasureString(name, nameFont).Width, measure.MeasureString(kind, kindFont).Width) + (2 * PadX);
            return new Box(name, kind, width, nameFont.GetHeight() + kindFont.GetHeight() + (2 * PadY));
        }

        double LabelWidth(Dependency dependency) =>
            Label(dependency) is { } label ? measure.MeasureString(label, labelFont).Width + 8 : 0;

        var subject = MakeBox(result.Subject);
        var users = result.UsedBy.Select(d => (Dependency: d, Box: MakeBox(d.Object))).ToList();
        var used = result.Uses.Select(d => (Dependency: d, Box: MakeBox(d.Object))).ToList();

        var leftWidth = users.Count == 0 ? 0 : users.Max(u => u.Box.Width);
        var rightWidth = used.Count == 0 ? 0 : used.Max(u => u.Box.Width);
        var leftGap = users.Count == 0 ? 0 : Math.Max(MinEdge, users.Max(u => LabelWidth(u.Dependency)) + 56);
        var rightGap = used.Count == 0 ? 0 : Math.Max(MinEdge, used.Max(u => LabelWidth(u.Dependency)) + 56);

        double ColumnHeight(IReadOnlyList<(Dependency Dependency, Box Box)> column) =>
            column.Count == 0 ? 0 : column.Sum(c => c.Box.Height) + (RowGap * (column.Count - 1));

        var height = Math.Max(subject.Height, Math.Max(ColumnHeight(users), ColumnHeight(used))) + (2 * Margin);
        var subjectX = Margin + leftWidth + leftGap;
        var rightX = subjectX + subject.Width + rightGap;
        var width = rightX + rightWidth + Margin;
        subject.Place(subjectX, (height - subject.Height) / 2);
        Stack(users, Margin, leftWidth, height);
        Stack(used, rightX, rightWidth, height);

        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(width);
        page.Height = XUnit.FromPoint(height);
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            foreach (var labelOnly in new[] { false, true })
            {
                foreach (var (dependency, box) in users)
                {
                    Edge(graphics, labelFont, dependency, box.Right, box.Middle, subject.X, subject.Middle, atStart: true, labelOnly);
                }

                foreach (var (dependency, box) in used)
                {
                    Edge(graphics, labelFont, dependency, subject.Right, subject.Middle, box.X, box.Middle, atStart: false, labelOnly);
                }
            }

            foreach (var box in users.Select(u => u.Box).Concat(used.Select(u => u.Box)))
            {
                Draw(graphics, box, nameFont, kindFont, 1);
            }

            Draw(graphics, subject, nameFont, kindFont, 2.5);
        }

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return new DiagramImage(stream.ToArray(), width, height);
    }

    // The boxes of a column, centered in the column and together centered in the height.
    private static void Stack(IReadOnlyList<(Dependency Dependency, Box Box)> column, double x, double columnWidth, double height)
    {
        var y = (height - (column.Sum(c => c.Box.Height) + (RowGap * Math.Max(0, column.Count - 1)))) / 2;
        foreach (var (_, box) in column)
        {
            box.Place(x + ((columnWidth - box.Width) / 2), y);
            y += box.Height + RowGap;
        }
    }

    private static string? Label(Dependency dependency) =>
        dependency.Reason == DependencyReason.ForeignKey && !string.IsNullOrEmpty(dependency.Detail) ? dependency.Detail : null;

    private static void Draw(XGraphics graphics, Box box, XFont nameFont, XFont kindFont, double border)
    {
        var inset = border / 2;
        graphics.DrawRoundedRectangle(new XPen(Stroke, border), new XSolidBrush(Fill), box.X + inset, box.Y + inset, box.Width - border, box.Height - border, 8, 8);
        var nameHeight = nameFont.GetHeight();
        var brush = new XSolidBrush(Ink);
        graphics.DrawString(box.Name, nameFont, brush, new XRect(box.X, box.Y + PadY, box.Width, nameHeight), XStringFormats.Center);
        graphics.DrawString(box.Kind, kindFont, brush, new XRect(box.X, box.Y + PadY + nameHeight, box.Width, kindFont.GetHeight()), XStringFormats.Center);
    }

    // A curve that leaves and arrives horizontally, as Mermaid draws it, with the arrow head at the end. The label
    // of a foreign key sits on a straight part next to the other object; labels are drawn after all lines, so no
    // line crosses a label.
    private static void Edge(XGraphics graphics, XFont labelFont, Dependency dependency, double x1, double y1, double x2, double y2, bool atStart, bool labelOnly)
    {
        var end = x2 - Arrow;
        var label = Label(dependency);
        var labelSize = label is null ? default : graphics.MeasureString(label, labelFont);
        var labelWidth = label is null ? 0 : labelSize.Width + 6;
        var straight = label is null ? 0 : LabelOffset + labelWidth + 4;
        var curveStart = atStart ? x1 + straight : x1;
        var curveEnd = atStart ? end : end - straight;
        if (!labelOnly)
        {
            var pen = new XPen(Ink, 1);
            if (dependency.Reason == DependencyReason.SourceText)
            {
                pen.DashStyle = XDashStyle.Dash;
            }

            var bend = (curveEnd - curveStart) / 2;
            graphics.DrawLine(pen, x1, y1, curveStart, y1);
            graphics.DrawBezier(pen, curveStart, y1, curveStart + bend, y1, curveEnd - bend, y2, curveEnd, y2);
            graphics.DrawLine(pen, curveEnd, y2, end, y2);
            graphics.DrawPolygon(new XSolidBrush(Ink), new[] { new XPoint(x2, y2), new XPoint(end, y2 - (Arrow / 2)), new XPoint(end, y2 + (Arrow / 2)) }, XFillMode.Winding);
            return;
        }

        if (label is null)
        {
            return;
        }

        var x = atStart ? x1 + LabelOffset : end - LabelOffset - labelWidth;
        var y = atStart ? y1 : y2;
        var rect = new XRect(x, y - (labelSize.Height / 2) - 1, labelWidth, labelSize.Height + 2);
        graphics.DrawRectangle(new XSolidBrush(LabelFill), rect);
        graphics.DrawString(label, labelFont, new XSolidBrush(Ink), rect, XStringFormats.Center);
    }

    private sealed class Box(string name, string kind, double width, double height)
    {
        public string Name { get; } = name;

        public string Kind { get; } = kind;

        public double Width { get; } = width;

        public double Height { get; } = height;

        public double X { get; private set; }

        public double Y { get; private set; }

        public double Right => X + Width;

        public double Middle => Y + (Height / 2);

        public void Place(double x, double y)
        {
            X = x;
            Y = y;
        }
    }
}
