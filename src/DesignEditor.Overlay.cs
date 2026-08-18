using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DesignLayout = ArxisStudio.Attached.Layout;
using DesignInteraction = ArxisStudio.Attached.DesignInteraction;
using ArxisStudio.Controls;
using ArxisStudio.Guides;
using ArxisStudio.Placement;
using ArxisStudio.States;

namespace ArxisStudio;

// Пересборка состояния оверлея выделения.
// Часть DesignEditor; общее описание типа — в DesignEditor.cs.
public partial class DesignEditor
{
    private bool TryGetSelectedDesignBounds(
        out Rect bounds,
        out int selectedCount,
        out DesignEditorItem? primaryItem,
        out Control? primaryControl,
        out IReadOnlyList<SelectionAdornerInfo> secondaryAdorners,
        out bool hasMultipleNestedSelection,
        out bool hasMultipleContainerSelection,
        out bool hasGroupSelection)
    {
        bounds = default;
        selectedCount = 0;
        primaryItem = null;
        primaryControl = null;
        secondaryAdorners = Array.Empty<SelectionAdornerInfo>();
        hasMultipleNestedSelection = false;
        hasMultipleContainerSelection = false;
        hasGroupSelection = false;

        var items = SelectedItems;
        if (items == null || items.Count == 0)
            return false;

        var perTargetBounds = new List<SelectionAdornerInfo>();
        var hasBounds = false;
        var containerTargetCount = 0;
        var nestedTargetCount = 0;
        double left = 0;
        double top = 0;
        double right = 0;
        double bottom = 0;

        foreach (var item in items)
        {
            var container = ContainerFromItem(item) as DesignEditorItem;
            if (container == null && item is DesignEditorItem directItem)
                container = directItem;

            if (container == null)
                continue;

            foreach (var selectionTarget in ResolveSelectionTargets(container))
            {
                if (!TryGetDesignBounds(selectionTarget, out var itemBounds))
                    continue;

                selectedCount++;
                primaryItem ??= container;
                primaryControl ??= selectionTarget;

                // Контейнером считается любой DesignEditorItem, а не только владелец:
                // вложенные контейнеры должны попадать в группу контейнеров,
                // иначе их множественный выбор рисуется как nested-группа.
                if (selectionTarget is DesignEditorItem)
                    containerTargetCount++;
                else
                    nestedTargetCount++;

                perTargetBounds.Add(new SelectionAdornerInfo
                {
                    Container = container,
                    Target = selectionTarget,
                    Bounds = itemBounds,
                    Role = SelectionAdornerRole.Secondary,
                    ResizePolicy = GetResizePolicy(selectionTarget),
                    MovePolicy = GetEffectiveMovePolicy(selectionTarget)
                });

                if (!hasBounds)
                {
                    left = itemBounds.Left;
                    top = itemBounds.Top;
                    right = itemBounds.Right;
                    bottom = itemBounds.Bottom;
                    hasBounds = true;
                    continue;
                }

                left = Math.Min(left, itemBounds.Left);
                top = Math.Min(top, itemBounds.Top);
                right = Math.Max(right, itemBounds.Right);
                bottom = Math.Max(bottom, itemBounds.Bottom);
            }
        }

        if (!hasBounds)
            return false;

        bounds = new Rect(left, top, right - left, bottom - top);
        hasMultipleContainerSelection = selectedCount > 1 && containerTargetCount == selectedCount;
        hasMultipleNestedSelection = selectedCount > 1 && nestedTargetCount == selectedCount;

        // Группа рисуется одной рамкой: контуры участников по отдельности сказали бы,
        // что элементов несколько, а жест применяется к ним как к одному.
        hasGroupSelection = hasMultipleNestedSelection
            && IsWholeGroupSelected(perTargetBounds.Select(a => a.Target!).ToList());

        if (hasMultipleNestedSelection && !hasGroupSelection)
        {
            foreach (var adorner in perTargetBounds)
            {
                adorner.ShowHandles = true;
                adorner.IsInteractive = true;
            }
        }

        secondaryAdorners = hasMultipleNestedSelection && !hasGroupSelection
            ? perTargetBounds
            : Array.Empty<SelectionAdornerInfo>();
        return true;
    }

    private void UpdateSelectionOverlayState()
    {
        SyncEnteredGroup();

        if (TryGetSelectedDesignBounds(
                out var bounds,
                out var selectedCount,
                out var primaryItem,
                out var primaryControl,
                out var secondaryAdorners,
                out var hasMultipleNestedSelection,
                out var hasMultipleContainerSelection,
                out var hasGroupSelection))
        {
            CleanupSelectionTargets();
            SelectionBounds = bounds;
            SecondarySelectionAdorners = secondaryAdorners;
            HasSingleSelection = selectedCount == 1;
            HasMultipleSelection = selectedCount > 1;
            HasMultipleNestedSelection = hasMultipleNestedSelection && !hasGroupSelection;
            HasMultipleContainerSelection = hasMultipleContainerSelection;
            HasGroupSelection = hasGroupSelection;
            ShowsGroupFrame = hasMultipleContainerSelection || hasGroupSelection;
            _primarySelectionItem = primaryItem;
            _primarySelectionControl = primaryControl;
            UpdatePrimaryPlacementReadout(primaryControl);
            ApplySelectionSnapshot(CreateSelectionTargetsSnapshot(primaryItem, primaryControl));
            SyncSelectedTargetSubscriptions();
            UpdateSelectionAdornerPolicies();
            return;
        }

        _selectedTargets.Clear();
        SelectionBounds = default;
        SecondarySelectionAdorners = Array.Empty<SelectionAdornerInfo>();
        HasSingleSelection = false;
        HasMultipleSelection = false;
        HasMultipleNestedSelection = false;
        HasMultipleContainerSelection = false;
        HasGroupSelection = false;
        ShowsGroupFrame = false;
        _primarySelectionItem = null;
        _primarySelectionControl = null;
        UpdatePrimaryPlacementReadout(null);
        ApplySelectionSnapshot(Array.Empty<DesignSelectionTarget>());
        SyncSelectedTargetSubscriptions();
        UpdateSelectionAdornerPolicies();
    }

    /// <summary>
    /// Обновляет диагностический вывод о размещении primary target.
    /// </summary>
    private void UpdatePrimaryPlacementReadout(Control? primaryControl)
    {
        if (primaryControl == null)
        {
            PrimarySelectionPlacement = null;
            PrimarySelectionMovePolicy = ArxisStudio.Attached.MovePolicy.None;
            PrimarySelectionResizePolicy = ArxisStudio.Attached.ResizePolicy.None;
            return;
        }

        PrimarySelectionPlacement = GetPlacementStrategy(primaryControl).Name;
        PrimarySelectionMovePolicy = GetEffectiveMovePolicy(primaryControl);
        PrimarySelectionResizePolicy = GetResizePolicy(primaryControl);
    }
}
