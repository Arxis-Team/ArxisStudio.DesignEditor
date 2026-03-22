using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio;

internal sealed class GroupDragOperation
{
    private readonly IReadOnlyList<GroupDragTarget> _targets;
    private Vector _accumulatedDelta;

    private GroupDragOperation(DesignEditorItem sourceContainer, Control sourceTarget, IReadOnlyList<GroupDragTarget> targets)
    {
        SourceContainer = sourceContainer;
        SourceTarget = sourceTarget;
        _targets = targets;
        _accumulatedDelta = Vector.Zero;
    }

    public DesignEditorItem SourceContainer { get; }
    public Control SourceTarget { get; }

    public static GroupDragOperation? TryCreate(DesignEditor editor, DesignEditorItem sourceContainer, Control sourceTarget)
    {
        var targets = new List<GroupDragTarget>();
        var items = editor.SelectedItems;
        if (items == null || items.Count == 0)
            return null;

        foreach (var item in items)
        {
            var container = editor.ContainerFromItem(item) as DesignEditorItem;
            if (container == null && item is DesignEditorItem directItem)
                container = directItem;

            if (container == null || !container.IsDraggable)
                continue;

            foreach (var target in editor.ResolveSelectionTargets(container))
            {
                if (ReferenceEquals(container, sourceContainer) && ReferenceEquals(target, sourceTarget))
                    continue;
                if (editor.GetMovePolicy(target) == ArxisStudio.Attached.MovePolicy.None)
                    continue;

                targets.Add(new GroupDragTarget(target, editor.GetDesignPosition(target)));
            }
        }

        return targets.Count > 0
            ? new GroupDragOperation(sourceContainer, sourceTarget, targets)
            : null;
    }

    public bool CanHandle(DesignEditorItem sourceContainer)
    {
        return ReferenceEquals(sourceContainer, SourceContainer);
    }

    public void Update(DesignEditor editor, Vector frameDelta)
    {
        _accumulatedDelta += frameDelta;

        for (var i = 0; i < _targets.Count; i++)
        {
            var snapshot = _targets[i];
            var filteredDelta = editor.ApplyMovePolicy(snapshot.Target, _accumulatedDelta);
            editor.SetDesignPosition(snapshot.Target, snapshot.InitialPosition + filteredDelta);
        }
    }
}

internal readonly struct GroupDragTarget
{
    public GroupDragTarget(Control target, Point initialPosition)
    {
        Target = target;
        InitialPosition = initialPosition;
    }

    public Control Target { get; }
    public Point InitialPosition { get; }
}
