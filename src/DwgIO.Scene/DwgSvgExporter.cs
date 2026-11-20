using System.Globalization;
using System.Xml;
using DwgIO.Internal;
using DwgIO.Model;

namespace DwgIO.Scene;

/// <summary>
/// Draws the model space of a drawing as SVG: LINE, CIRCLE, ARC and POINT entities in the top view, with the
/// drawing's X to the right and Y up, in the entities' ACI colours. The <c>viewBox</c> is the bounding box of
/// everything drawn plus a margin, in drawing units with y flipped, so the output contains every exported entity.
/// </summary>
/// <remarks>
/// This is the first step of issue #32: polylines, curves and text join as the core decodes them. SVG is a 2D
/// format; the drawing's Z is dropped, so entities off the XY plane are drawn flattened onto it.
/// </remarks>
public static class DwgSvgExporter
{
    /// <summary>Writes the model space of <paramref name="document"/> to a new SVG file at <paramref name="path"/>.</summary>
    public static void WriteFile(DwgDocument document, string path, DwgSvgOptions? options = null)
    {
        AtomicOutput.Write(path, stream => Write(document, stream, options));
    }

    /// <summary>Writes the model space of <paramref name="document"/> to a stream. The stream stays open.</summary>
    public static void Write(DwgDocument document, Stream output, DwgSvgOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        using var writer = XmlWriter.Create(output, new XmlWriterSettings
        {
            Indent = true,
            Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        });
        Write(document, writer, options);
    }

    /// <summary>Writes the model space of <paramref name="document"/> to an XML writer.</summary>
    public static void Write(DwgDocument document, XmlWriter writer, DwgSvgOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(writer);
        options ??= new DwgSvgOptions();
        if (!double.IsFinite(options.Scale) || options.Scale <= 0)
        {
            throw new ArgumentException("DwgSvgOptions.Scale must be finite and positive.", nameof(options));
        }

        if (!double.IsFinite(options.StrokeWidth) || options.StrokeWidth <= 0)
        {
            throw new ArgumentException("DwgSvgOptions.StrokeWidth must be finite and positive.", nameof(options));
        }

        var shapes = Collect(document, options.Scale);
        (double minX, double minY, double width, double height) = ViewBox(shapes);
        // Margins and markers are pixel values: they stay fixed while the scale zooms the drawing.
        double margin = Math.Max(0.02 * Math.Max(width, height), 8.0);
        if (!IsUsable(minX - margin, minY - margin, width + 2 * margin, height + 2 * margin))
        {
            throw new ArgumentException("Drawing extents at this scale exceed the SVG coordinate range.", nameof(options));
        }

        double markerRadius = Math.Max(0.015 * Math.Max(width, height), 3.0);

        writer.WriteStartDocument();
        writer.WriteStartElement("svg", "http://www.w3.org/2000/svg");
        writer.WriteAttributeString("viewBox", $"{Num(minX - margin)} {Num(minY - margin)} {Num(width + 2 * margin)} {Num(height + 2 * margin)}");
        writer.WriteAttributeString("width", Num(width + 2 * margin));
        writer.WriteAttributeString("height", Num(height + 2 * margin));

        writer.WriteStartElement("rect");
        writer.WriteAttributeString("x", Num(minX - margin));
        writer.WriteAttributeString("y", Num(minY - margin));
        writer.WriteAttributeString("width", Num(width + 2 * margin));
        writer.WriteAttributeString("height", Num(height + 2 * margin));
        writer.WriteAttributeString("fill", options.Background);
        writer.WriteEndElement();

        writer.WriteStartElement("g");
        writer.WriteAttributeString("fill", "none");
        writer.WriteAttributeString("stroke", "#000000");
        writer.WriteAttributeString("stroke-width", Num(options.StrokeWidth));
        writer.WriteAttributeString("stroke-linecap", "round");

        foreach (var shape in shapes)
        {
            string stroke = ColourOf(shape.ColorIndex);
            switch (shape)
            {
                case SvgLine line:
                    writer.WriteStartElement("line");
                    writer.WriteAttributeString("x1", Num(line.X1));
                    writer.WriteAttributeString("y1", Num(line.Y1));
                    writer.WriteAttributeString("x2", Num(line.X2));
                    writer.WriteAttributeString("y2", Num(line.Y2));
                    writer.WriteAttributeString("stroke", stroke);
                    writer.WriteEndElement();
                    break;
                case SvgCircle circle:
                    writer.WriteStartElement("circle");
                    writer.WriteAttributeString("cx", Num(circle.Cx));
                    writer.WriteAttributeString("cy", Num(circle.Cy));
                    writer.WriteAttributeString("r", Num(circle.Radius));
                    writer.WriteAttributeString("stroke", stroke);
                    writer.WriteEndElement();
                    break;
                case SvgArc arc:
                    (double x1, double y1, double x2, double y2) = ArcEndpoints(arc);
                    // The drawing's CCW arc becomes a CW trace in SVG's y-down space, hence sweep 0; the y flip
                    // is baked into the endpoints already.
                    writer.WriteStartElement("path");
                    writer.WriteAttributeString(
                        "d",
                        FormattableString.Invariant(
                            $"M {Num(x1)} {Num(y1)} A {Num(arc.Radius)} {Num(arc.Radius)} 0 {(arc.Span > Math.PI ? 1 : 0):D} 0 {Num(x2)} {Num(y2)}"));
                    writer.WriteAttributeString("stroke", stroke);
                    writer.WriteEndElement();
                    break;
                case SvgPoint:
                    writer.WriteStartElement("circle");
                    writer.WriteAttributeString("cx", Num(((SvgPoint)shape).X));
                    writer.WriteAttributeString("cy", Num(((SvgPoint)shape).Y));
                    writer.WriteAttributeString("r", Num(markerRadius));
                    writer.WriteAttributeString("fill", stroke);
                    writer.WriteAttributeString("stroke", "none");
                    writer.WriteEndElement();
                    break;
            }
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private abstract record SvgShape(int ColorIndex);

    private sealed record SvgLine(double X1, double Y1, double X2, double Y2, int ColorIndex) : SvgShape(ColorIndex);

    private sealed record SvgCircle(double Cx, double Cy, double Radius, int ColorIndex) : SvgShape(ColorIndex);

    // Start and span are normalised (span > 0); the endpoints follow in SVG coordinates.
    private sealed record SvgArc(double Cx, double Cy, double Radius, double Start, double Span, int ColorIndex)
        : SvgShape(ColorIndex);

    private sealed record SvgPoint(double X, double Y, int ColorIndex) : SvgShape(ColorIndex);

    private static List<SvgShape> Collect(DwgDocument document, double scale)
    {
        var shapes = new List<SvgShape>();
        foreach (var entity in document.Entities)
        {
            if (!entity.IsModelSpace)
            {
                continue;
            }

            switch (entity.Kind, entity.Geometry)
            {
                case (DwgEntityKind.Line, [var x1, var y1, var z1, var x2, var y2, var z2]):
                    if (IsUsable(x1 * scale, y1 * scale, x2 * scale, y2 * scale))
                    {
                        shapes.Add(new SvgLine(x1 * scale, -y1 * scale, x2 * scale, -y2 * scale, entity.ColorIndex));
                    }
                    else
                    {
                        Skip(document, entity, "non-finite coordinates");
                    }

                    break;
                case (DwgEntityKind.Circle, [var cx, var cy, var cz, var r]):
                    if (IsUsable((Math.Abs(cx) + r) * scale, (Math.Abs(cy) + r) * scale) && r > 0)
                    {
                        shapes.Add(new SvgCircle(cx * scale, -cy * scale, r * scale, entity.ColorIndex));
                    }
                    else
                    {
                        Skip(document, entity, "non-finite or non-positive radius");
                    }

                    break;
                case (DwgEntityKind.Arc, [var cx, var cy, _, var r, var start, var end]):
                    if (IsUsable((Math.Abs(cx) + r) * scale, (Math.Abs(cy) + r) * scale, start, end) && r > 0)
                    {
                        var (arcStart, span) = ArcAngles.Normalize(start, end);
                        shapes.Add(span == Math.Tau
                            ? new SvgCircle(cx * scale, -cy * scale, r * scale, entity.ColorIndex)
                            : new SvgArc(cx * scale, -cy * scale, r * scale, arcStart, span, entity.ColorIndex));
                    }
                    else
                    {
                        Skip(document, entity, "non-finite or non-positive radius");
                    }

                    break;
                case (DwgEntityKind.Point, [var x, var y, _]):
                    if (IsUsable(x * scale, y * scale))
                    {
                        shapes.Add(new SvgPoint(x * scale, -y * scale, entity.ColorIndex));
                    }
                    else
                    {
                        Skip(document, entity, "non-finite coordinates");
                    }

                    break;
            }
        }

        return shapes;
    }

    private static void Skip(DwgDocument document, DwgEntity entity, string reason) =>
        document.ReportDiagnostic($"svg: entity {entity.Handle:X} ({entity.TypeName}) skipped: {reason}");

    private static bool IsUsable(params double[] values) => values.All(double.IsFinite);

    private static (double X1, double Y1, double X2, double Y2) ArcEndpoints(SvgArc arc) =>
        (
            arc.Cx + arc.Radius * Math.Cos(arc.Start),
            arc.Cy - arc.Radius * Math.Sin(arc.Start),
            arc.Cx + arc.Radius * Math.Cos(arc.Start + arc.Span),
            arc.Cy - arc.Radius * Math.Sin(arc.Start + arc.Span)
        );

    private static (double MinX, double MinY, double Width, double Height) ViewBox(List<SvgShape> shapes)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var shape in shapes)
        {
            switch (shape)
            {
                case SvgLine line:
                    Expand(line.X1, line.Y1);
                    Expand(line.X2, line.Y2);
                    break;
                case SvgCircle circle:
                    Expand(circle.Cx - circle.Radius, circle.Cy - circle.Radius);
                    Expand(circle.Cx + circle.Radius, circle.Cy + circle.Radius);
                    break;
                case SvgArc arc:
                    (double x1, double y1, double x2, double y2) = ArcEndpoints(arc);
                    Expand(x1, y1);
                    Expand(x2, y2);
                    // The extremes of the four cardinal directions lie inside the arc when their angle falls
                    // within its span; without them the view box could cut through the drawn curve.
                    for (int k = 0; k < 4; k++)
                    {
                        double angle = k * Math.PI / 2.0;
                        if (ArcAngles.PositiveAngle(angle - arc.Start) <= arc.Span)
                        {
                            Expand(arc.Cx + arc.Radius * Math.Cos(angle), -Math.Sin(angle) * arc.Radius + arc.Cy);
                        }
                    }

                    break;
                case SvgPoint point:
                    Expand(point.X, point.Y);
                    break;
            }
        }

        if (minX > maxX)
        {
            return (0, 0, 0, 0);
        }

        return (minX, minY, maxX - minX, maxY - minY);

        void Expand(double x, double y)
        {
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }
    }

    private static string ColourOf(int colorIndex)
    {
        // BYBLOCK (0) and BYLAYER (256) resolve to the default layer colour, which is 7; on a light background
        // AutoCAD renders 7 (white) as black.
        int resolved = colorIndex is > 0 and < 256 ? colorIndex : 7;
        if (resolved == 7)
        {
            return "#000000";
        }

        (byte r, byte g, byte b) = DwgAciPalette.RgbOf(resolved);
        return FormattableString.Invariant($"#{r:X2}{g:X2}{b:X2}");
    }

    private static string Num(double value)
    {
        if (value == 0.0)
        {
            value = 0.0;   // normalise -0
        }

        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
