using System.Numerics;
using System.Text.Json;
using ChunPaint.Core;

namespace ChunPaint.Core.Tests;

[TestClass]
public sealed class EditableObjectTests
{
    [TestMethod]
    public void LayerIdIsAnOptionalTrailingRecordPropertyAndFactoriesPropagateIt()
    {
        var property = typeof(EditableObject).GetProperty(nameof(EditableObject.LayerId));
        Assert.IsNotNull(property);
        Assert.AreEqual(typeof(string), property.PropertyType);

        var constructor = typeof(EditableObject).GetConstructors().Single();
        var parameters = constructor.GetParameters();
        var layerParameter = parameters.Single(parameter =>
            string.Equals(parameter.Name, nameof(EditableObject.LayerId), StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(parameters[^1], layerParameter, "LayerId must remain the optional tail field for v1 JSON compatibility.");
        Assert.IsTrue(layerParameter.IsOptional);
        Assert.IsNull(layerParameter.DefaultValue);

        var text = EditableObject.CreateText(
            new ObjectBounds(0, 0, 2, 2), "text", id: "text", layerId: "layer-a");
        var shape = EditableObject.CreateShape(
            EditableShapeKind.Rectangle, new ObjectBounds(0, 0, 2, 2), id: "shape", layerId: "layer-a");
        Assert.AreEqual("layer-a", text.LayerId);
        Assert.AreEqual("layer-a", shape.LayerId);
    }

    [TestMethod]
    public void TextObjectPreservesFormattingAndTransforms()
    {
        var style = new TextObjectStyle(
            "Meiryo",
            32,
            Bold: true,
            Italic: true,
            Underline: true,
            Strikethrough: true,
            Alignment: EditableTextAlignment.Center,
            Foreground: new Rgba32(10, 20, 30),
            HasBackground: true,
            Background: new Rgba32(240, 230, 220));
        var text = EditableObject.CreateText(new ObjectBounds(10, 20, 40, 30), "一行目\n二行目", style, "text-1");

        var moved = text.MoveBy(new Vector2(100, 100), new ObjectBounds(0, 0, 100, 100));
        var resized = moved.Resize(SelectionHandle.BottomRight, new Vector2(-10, -5), new ObjectBounds(0, 0, 100, 100));
        var rotated = resized.RotateBy(450);

        Assert.AreEqual(EditableObjectKind.Text, rotated.Kind);
        Assert.AreEqual("text-1", rotated.Id);
        Assert.AreEqual("一行目\n二行目", rotated.Text);
        Assert.AreEqual(style, rotated.TextStyle);
        Assert.AreEqual(EditableObjectTransform.NormalizeDegrees(450), rotated.RotationDegrees);
        Assert.IsTrue(rotated.Bounds.Right <= 100);
        Assert.IsTrue(rotated.Bounds.Bottom <= 100);
    }

    [TestMethod]
    public void ShapeObjectScalesLineEndpointsAndKeepsStyle()
    {
        var style = new ShapeObjectStyle(
            new Rgba32(255, 0, 0, 200),
            new Rgba32(0, 255, 0, 100),
            7,
            true,
            true,
            ShapeStrokeKind.Crayon,
            ShapeFillKind.Watercolor);
        var shape = EditableObject.CreateShape(
            EditableShapeKind.Heart,
            new ObjectBounds(10, 10, 40, 20),
            style,
            new ObjectPoint(10, 20),
            new ObjectPoint(50, 30),
            "shape-1");

        var resized = shape.Resize(SelectionHandle.Right, new Vector2(20, 0), new ObjectBounds(0, 0, 100, 100));

        Assert.AreEqual(EditableShapeKind.Heart, resized.Shape);
        Assert.AreEqual(style, resized.ShapeStyle);
        Assert.AreEqual(new ObjectPoint(10, 20), resized.StartPoint);
        Assert.AreEqual(new ObjectPoint(70, 30), resized.EndPoint);
        Assert.AreEqual("shape-1", resized.Id);
    }

    [TestMethod]
    public void ResizeCanPreserveAspectRatioAndResizeFromCenter()
    {
        var shape = EditableObject.CreateShape(
            EditableShapeKind.Rectangle,
            new ObjectBounds(20, 30, 40, 20),
            id: "shape");

        var resized = shape.Resize(
            SelectionHandle.BottomRight,
            new Vector2(10, 5),
            new ObjectBounds(0, 0, 200, 200),
            preserveAspectRatio: true,
            fromCenter: true);

        Assert.AreEqual(new ObjectBounds(10, 25, 60, 30), resized.Bounds);
    }

    [TestMethod]
    public void ResizePreservesAspectRatioAtCanvasEdge()
    {
        var shape = EditableObject.CreateShape(
            EditableShapeKind.Rectangle,
            new ObjectBounds(20, 30, 40, 20),
            id: "shape");

        var resized = shape.Resize(
            SelectionHandle.BottomRight,
            new Vector2(100, 100),
            new ObjectBounds(0, 0, 100, 100),
            preserveAspectRatio: true);

        Assert.AreEqual(new ObjectBounds(20, 30, 80, 40), resized.Bounds);
        Assert.AreEqual(2, resized.Bounds.Width / resized.Bounds.Height);
    }

    [TestMethod]
    public void ResizeFromCenterKeepsCenterAtCanvasEdge()
    {
        var shape = EditableObject.CreateShape(
            EditableShapeKind.Rectangle,
            new ObjectBounds(20, 30, 40, 20),
            id: "shape");

        var resized = shape.Resize(
            SelectionHandle.Right,
            new Vector2(100, 0),
            new ObjectBounds(0, 0, 100, 100),
            fromCenter: true);

        Assert.AreEqual(new ObjectBounds(0, 30, 80, 20), resized.Bounds);
        Assert.AreEqual(shape.Bounds.Center, resized.Bounds.Center);
    }

    [TestMethod]
    public void ResizePreservesAspectRatioAtMinimumSize()
    {
        var shape = EditableObject.CreateShape(
            EditableShapeKind.Rectangle,
            new ObjectBounds(20, 30, 40, 20),
            id: "shape");

        var resized = shape.Resize(
            SelectionHandle.Right,
            new Vector2(-100, 0),
            new ObjectBounds(0, 0, 100, 100),
            preserveAspectRatio: true);

        Assert.AreEqual(2, resized.Bounds.Width);
        Assert.AreEqual(1, resized.Bounds.Height);
        Assert.AreEqual(2, resized.Bounds.Width / resized.Bounds.Height);
    }

    [TestMethod]
    public void SnapDegreesUsesFifteenDegreeIncrements()
    {
        Assert.AreEqual(30, EditableObjectTransform.SnapDegrees(23));
        Assert.AreEqual(-15, EditableObjectTransform.SnapDegrees(-8));
    }

    [TestMethod]
    public void ObjectStateIsJsonSerializable()
    {
        var objects = new[]
        {
            EditableObject.CreateText(new ObjectBounds(1, 2, 30, 20), "hello", new TextObjectStyle(Foreground: new Rgba32(1, 2, 3)), "text"),
            EditableObject.CreateShape(
                EditableShapeKind.Lightning,
                new ObjectBounds(4, 5, 12, 14),
                new ShapeObjectStyle(
                    OutlineColor: new Rgba32(5, 6, 7),
                    DrawFill: true,
                    OutlineKind: ShapeStrokeKind.Pencil,
                    FillKind: ShapeFillKind.Crayon),
                id: "shape")
        };

        var json = JsonSerializer.Serialize(objects);
        var restored = JsonSerializer.Deserialize<EditableObject[]>(json);

        Assert.IsNotNull(restored);
        Assert.HasCount(2, restored);
        Assert.AreEqual(objects[0], restored[0]);
        Assert.AreEqual(objects[1], restored[1]);
    }

    [TestMethod]
    public void ValidateRejectsMissingObjectSpecificState()
    {
        var invalid = new EditableObject("invalid", EditableObjectKind.Text, new ObjectBounds(0, 0, 1, 1));

        Assert.ThrowsExactly<ArgumentException>(() => invalid.Validate());
    }

    [TestMethod]
    public void ValidateRejectsUndefinedPersistedEnumValues()
    {
        var invalidAlignment = EditableObject.CreateText(
            new ObjectBounds(0, 0, 1, 1),
            "text",
            new TextObjectStyle(Alignment: (EditableTextAlignment)999));
        var invalidShape = EditableObject.CreateShape(
            (EditableShapeKind)999,
            new ObjectBounds(0, 0, 1, 1));
        var invalidOutline = EditableObject.CreateShape(
            EditableShapeKind.Rectangle,
            new ObjectBounds(0, 0, 1, 1),
            new ShapeObjectStyle(OutlineKind: (ShapeStrokeKind)999));
        var invalidFill = EditableObject.CreateShape(
            EditableShapeKind.Rectangle,
            new ObjectBounds(0, 0, 1, 1),
            new ShapeObjectStyle(FillKind: (ShapeFillKind)999));

        Assert.ThrowsExactly<ArgumentException>(invalidAlignment.Validate);
        Assert.ThrowsExactly<ArgumentException>(invalidShape.Validate);
        Assert.ThrowsExactly<ArgumentException>(invalidOutline.Validate);
        Assert.ThrowsExactly<ArgumentException>(invalidFill.Validate);
    }
}
