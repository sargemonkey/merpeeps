using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Merpeeps.Core.Layout;
using Merpeeps.Core.Model;

namespace Merpeeps.App;

internal enum StencilGlyph
{
    Class, Entity, Note, Group, Participant, Actor, Message, Fragment, Slice, Point, Line, Bar,
    Axis, Curve, Tree, Section, Period, Task, Event, Packet, Column, Card, Requirement, Commit,
    Flow, Treemap, Block, Spacer, Arrow, Server, Junction, System, Container, Component,
    Deployment, Person, MapComponent, Annotation, Problem, Cause, DomainItem, Transition, Set, Overlap,
    Rule, Lane, Step, UseCase, FlowContainer, Connector, Timeframe, Data, Database, Call,
}

internal sealed class DiagramStencilPreview(StencilGlyph glyph) : Control
{
    internal StencilGlyph Glyph => glyph;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var ink = new SolidColorBrush(Color.Parse(dark ? "#D8D4E2" : "#3B3550"));
        var fill = new SolidColorBrush(Color.Parse(dark ? "#302448" : "#F1EDFF"));
        var line = new Pen(ink, 1.6);
        var dashed = new Pen(ink, 1.3, new DashStyle([3, 2], 0));
        var scale = Math.Min(Bounds.Width / 64, Bounds.Height / 44);
        if (scale <= 0) return;
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale));
        switch (glyph)
        {
            case StencilGlyph.Class:
            case StencilGlyph.Entity:
            case StencilGlyph.Requirement:
                Box(9, 3, 46, 38);
                context.FillRectangle(fill, new Rect(10, 4, 44, 10));
                Line(9, 14, 55, 14); Line(9, 27, 55, 27);
                if (glyph == StencilGlyph.Entity) { Line(22, 14, 22, 41); Line(42, 14, 42, 41); }
                if (glyph == StencilGlyph.Requirement) { Line(17, 8, 20, 11); Line(20, 11, 25, 6); }
                break;
            case StencilGlyph.Note:
            case StencilGlyph.Annotation:
            case StencilGlyph.Data:
                Polygon(new(12, 3), new(43, 3), new(53, 13), new(53, 41), new(12, 41));
                Line(43, 3, 43, 13); Line(43, 13, 53, 13);
                Line(20, 21, 43, 21); Line(20, 28, 43, 28); Line(20, 35, 36, 35);
                if (glyph == StencilGlyph.Data) { Line(18, 17, 15, 22); Line(15, 22, 18, 27); }
                break;
            case StencilGlyph.Group:
            case StencilGlyph.Fragment:
            case StencilGlyph.FlowContainer:
                context.DrawRectangle(null, dashed, new Rect(4, 3, 56, 38), 3, 3);
                if (glyph == StencilGlyph.Group) { Box(10, 1, 22, 9); }
                else if (glyph == StencilGlyph.Fragment) { Box(5, 4, 20, 10); Line(5, 25, 60, 25); }
                else { Box(11, 16, 15, 13); Box(38, 16, 15, 13); Arrow(26, 22, 38, 22); }
                break;
            case StencilGlyph.Participant:
                Box(13, 3, 38, 14); context.DrawLine(dashed, new(32, 17), new(32, 43));
                break;
            case StencilGlyph.Actor:
                context.DrawEllipse(fill, line, new Point(32, 9), 6, 6);
                Line(32, 15, 32, 29); Line(18, 22, 46, 22); Line(32, 29, 21, 40); Line(32, 29, 43, 40);
                break;
            case StencilGlyph.Person:
                context.DrawEllipse(fill, line, new Point(32, 9), 7, 7);
                Box(13, 17, 38, 25, 2);
                break;
            case StencilGlyph.Message:
            case StencilGlyph.Call:
                context.DrawLine(dashed, new(8, 2), new(8, 42));
                context.DrawLine(dashed, new(56, 2), new(56, 42));
                Arrow(8, 14, 56, 14);
                if (glyph == StencilGlyph.Call) { Box(52, 14, 8, 18); Arrow(52, 32, 8, 32); }
                break;
            case StencilGlyph.Slice:
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                    foreach (var part in ChartDiagramLayout.Sector(new(29, 23), 19, 0, -.8, 2.8))
                        switch (part.Kind)
                        {
                            case DrawingPathKind.Move: path.BeginFigure(new(part.End.X, part.End.Y), true); break;
                            case DrawingPathKind.Line: path.LineTo(new(part.End.X, part.End.Y)); break;
                            case DrawingPathKind.Cubic: path.CubicBezierTo(new(part.Control1.X, part.Control1.Y), new(part.Control2.X, part.Control2.Y), new(part.End.X, part.End.Y)); break;
                            case DrawingPathKind.Close: path.EndFigure(true); break;
                        }
                context.DrawGeometry(fill, line, geometry);
                break;
            case StencilGlyph.Point:
            case StencilGlyph.MapComponent:
                Line(9, 3, 9, 38); Line(9, 38, 58, 38);
                if (glyph == StencilGlyph.Point) { context.DrawLine(dashed, new(9, 19), new(39, 19)); context.DrawLine(dashed, new(39, 19), new(39, 38)); }
                context.DrawEllipse(fill, line, new Point(39, 19), 5, 5);
                break;
            case StencilGlyph.Line:
            case StencilGlyph.Bar:
                Line(6, 4, 6, 39); Line(6, 39, 60, 39);
                if (glyph == StencilGlyph.Bar) { Box(13, 24, 10, 15); Box(30, 12, 10, 27); Box(47, 18, 10, 21); }
                else { Line(10, 31, 25, 16); Line(25, 16, 40, 24); Line(40, 24, 57, 7); }
                break;
            case StencilGlyph.Axis:
            case StencilGlyph.Curve:
                var points = Enumerable.Range(0, 5).Select(i => new Point(32 + 20 * Math.Cos(i * Math.PI * .4 - Math.PI / 2),
                    22 + 19 * Math.Sin(i * Math.PI * .4 - Math.PI / 2))).ToArray();
                foreach (var point in points) context.DrawLine(glyph == StencilGlyph.Axis ? dashed : line, new(32, 22), point);
                if (glyph == StencilGlyph.Axis) context.DrawLine(new Pen(ink, 3), new(32, 22), points[0]);
                else Polygon(points.Select((p, i) => new Point(32 + (p.X - 32) * (i % 2 == 0 ? .9 : .6), 22 + (p.Y - 22) * (i % 2 == 0 ? .9 : .6))).ToArray());
                break;
            case StencilGlyph.Tree:
                Box(22, 2, 20, 12); Line(32, 14, 32, 24); Line(10, 24, 54, 24);
                Line(10, 24, 10, 31); Line(54, 24, 54, 31); Box(1, 31, 20, 12); Box(44, 31, 19, 12);
                break;
            case StencilGlyph.Section:
            case StencilGlyph.Lane:
                Box(3, 3, 58, 38); context.FillRectangle(fill, new Rect(4, 4, 56, 9)); Line(3, 13, 61, 13);
                if (glyph == StencilGlyph.Lane) { Line(22, 13, 22, 41); Line(42, 13, 42, 41); }
                break;
            case StencilGlyph.Period:
            case StencilGlyph.Event:
                Line(2, 34, 62, 34); Line(32, 18, 32, 38);
                if (glyph == StencilGlyph.Period) Box(12, 2, 40, 19);
                else context.DrawEllipse(fill, line, new Point(32, 18), 9, 9);
                break;
            case StencilGlyph.Task:
                context.DrawLine(dashed, new(5, 7), new(59, 7)); context.DrawLine(dashed, new(5, 37), new(59, 37));
                Box(13, 14, 39, 16, 3); break;
            case StencilGlyph.Packet:
                Box(3, 9, 58, 27); Line(19, 9, 19, 36); Line(45, 9, 45, 36); context.FillRectangle(fill, new Rect(20, 10, 24, 25));
                for (var x = 3; x <= 61; x += 8) Line(x, 4, x, 8);
                break;
            case StencilGlyph.Column:
                Box(17, 2, 30, 40); Line(17, 13, 47, 13); Box(21, 18, 22, 9, 2); Box(21, 30, 22, 8, 2); break;
            case StencilGlyph.Card:
                Box(9, 8, 46, 29, 3); Line(16, 16, 46, 16); Line(16, 24, 36, 24);
                context.DrawEllipse(ink, null, new Point(46, 30), 3, 3); break;
            case StencilGlyph.Commit:
                Line(2, 22, 62, 22); context.DrawEllipse(fill, line, new Point(32, 22), 10, 10); break;
            case StencilGlyph.Flow:
                context.DrawLine(new Pen(fill, 12), new(11, 29), new(52, 13));
                Box(4, 19, 7, 20); Box(52, 3, 7, 20); break;
            case StencilGlyph.Treemap:
                Box(3, 4, 58, 36); context.FillRectangle(fill, new Rect(4, 5, 33, 34)); Line(38, 4, 38, 40); Line(38, 22, 61, 22); break;
            case StencilGlyph.Block: Box(10, 6, 44, 32); break;
            case StencilGlyph.Spacer:
                context.DrawRectangle(null, dashed, new Rect(10, 6, 44, 32)); Arrow(15, 22, 49, 22); break;
            case StencilGlyph.Arrow: Polygon(new(5, 14), new(40, 14), new(40, 5), new(60, 22), new(40, 39), new(40, 30), new(5, 30)); break;
            case StencilGlyph.Server:
                IconSet.Draw(context, "server", new(12, 2, 40, 40), ink); break;
            case StencilGlyph.Database:
                ShapeRenderer.Draw(context, NodeShape.Cylinder, new(13, 2, 38, 40), fill, line); break;
            case StencilGlyph.Junction:
                Line(3, 22, 61, 22); Line(32, 2, 32, 42); context.DrawEllipse(ink, null, new Point(32, 22), 6, 6); break;
            case StencilGlyph.System:
            case StencilGlyph.Container:
            case StencilGlyph.Component:
            case StencilGlyph.Deployment:
                Box(8, 6, 48, 33, 3);
                if (glyph == StencilGlyph.Container) Box(14, 12, 36, 21);
                if (glyph == StencilGlyph.Component) { Box(3, 11, 11, 8); Box(3, 26, 11, 8); }
                if (glyph == StencilGlyph.Deployment) { Box(15, 15, 15, 16); Box(35, 15, 15, 16); }
                break;
            case StencilGlyph.Problem:
                Arrow(1, 22, 21, 22); Box(21, 7, 41, 30, 3); break;
            case StencilGlyph.Cause:
                Arrow(2, 37, 62, 37); Line(42, 37, 24, 6); Line(15, 21, 33, 21); break;
            case StencilGlyph.DomainItem:
                Line(32, 3, 32, 41); Line(4, 22, 60, 22); Box(7, 5, 19, 12, 2); break;
            case StencilGlyph.Transition:
                Box(3, 8, 16, 27, 2); Box(45, 8, 16, 27, 2); Arrow(20, 22, 44, 22); break;
            case StencilGlyph.Set: context.DrawEllipse(fill, line, new Point(32, 22), 20, 19); break;
            case StencilGlyph.Overlap:
                context.DrawEllipse(fill, line, new Point(24, 22), 18, 18);
                context.DrawEllipse(null, line, new Point(40, 22), 18, 18);
                context.DrawEllipse(ink, null, new Point(32, 22), 3, 3); break;
            case StencilGlyph.Rule:
                Line(2, 22, 62, 22); Box(16, 9, 32, 26, 12); break;
            case StencilGlyph.Step:
                Line(2, 2, 2, 42); Line(62, 2, 62, 42); Box(13, 10, 38, 24, 3); break;
            case StencilGlyph.UseCase:
                context.DrawEllipse(fill, line, new Point(32, 22), 28, 17); break;
            case StencilGlyph.Connector:
                Box(9, 14, 15, 16, 3); Box(40, 14, 15, 16, 3); Line(24, 22, 40, 22); break;
            case StencilGlyph.Timeframe:
                Line(2, 39, 62, 39); Box(12, 4, 40, 26, 3); Line(32, 30, 32, 39); Line(15, 11, 49, 11); break;
            default: throw new InvalidOperationException("Unknown stencil picture: " + glyph);
        }
        void Line(double x1, double y1, double x2, double y2) => context.DrawLine(line, new(x1, y1), new(x2, y2));
        void Box(double x, double y, double width, double height, double radius = 0) =>
            context.DrawRectangle(fill, line, new Rect(x, y, width, height), radius, radius);
        void Arrow(double x1, double y1, double x2, double y2)
        {
            Line(x1, y1, x2, y2);
            var direction = x2 >= x1 ? 1 : -1;
            Line(x2 - direction * 5, y2 - 4, x2, y2); Line(x2 - direction * 5, y2 + 4, x2, y2);
        }
        void Polygon(params Point[] points)
        {
            var shape = new StreamGeometry();
            using (var path = shape.Open())
            {
                path.BeginFigure(points[0], true);
                foreach (var point in points.Skip(1)) path.LineTo(point);
                path.EndFigure(true);
            }
            context.DrawGeometry(fill, line, shape);
        }
    }
}
