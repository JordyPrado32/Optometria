(function () {
    const instances = new Map();

    function parseId(value) {
        const parsed = Number.parseInt(value, 10);
        return Number.isFinite(parsed) ? parsed : null;
    }

    function getListKind(element) {
        return element?.dataset?.menuSortList ?? null;
    }

    function getParentId(element) {
        return parseId(element?.dataset?.parentId);
    }

    function clearDropState(root) {
        root.querySelectorAll(".menu-sort-item.is-dragging, .menu-sort-item.is-drop-target, .menu-sort-panel.is-drop-zone, .menu-sort-empty.is-drop-zone")
            .forEach((element) => {
                element.classList.remove("is-dragging", "is-drop-target", "is-drop-zone");
            });
    }

    window.optometriaMenuSort = {
        init: function (rootId, dotNetReference) {
            const root = document.getElementById(rootId);
            if (!root || !dotNetReference) {
                return;
            }

            const current = instances.get(rootId);
            if (current) {
                if (current.root === root) {
                    current.dotNetReference = dotNetReference;
                    return;
                }

                window.optometriaMenuSort.dispose(rootId);
            }

            const instance = {
                root,
                dotNetReference,
                draggedId: null
            };

            instance.dragStart = function (event) {
                const item = event.target.closest(".menu-sort-item[data-menu-id]");
                if (!item || !root.contains(item)) {
                    return;
                }

                instance.draggedId = parseId(item.dataset.menuId);
                if (!instance.draggedId) {
                    return;
                }

                event.dataTransfer.effectAllowed = "move";
                event.dataTransfer.setData("text/plain", instance.draggedId.toString());
                item.classList.add("is-dragging");
            };

            instance.dragOver = function (event) {
                if (!instance.draggedId || !root.contains(event.target)) {
                    return;
                }

                const dropArea = event.target.closest("[data-menu-sort-list]");
                if (!dropArea || !root.contains(dropArea)) {
                    return;
                }

                event.preventDefault();
                event.dataTransfer.dropEffect = "move";
                clearDropState(root);

                const targetItem = event.target.closest(".menu-sort-item[data-menu-id]");
                if (targetItem && root.contains(targetItem)) {
                    targetItem.classList.add("is-drop-target");
                    return;
                }

                dropArea.classList.add("is-drop-zone");
            };

            instance.drop = async function (event) {
                if (!instance.draggedId || !root.contains(event.target)) {
                    return;
                }

                const dropArea = event.target.closest("[data-menu-sort-list]");
                if (!dropArea || !root.contains(dropArea)) {
                    return;
                }

                event.preventDefault();
                event.stopPropagation();

                const draggedId = instance.draggedId;
                const targetItem = event.target.closest(".menu-sort-item[data-menu-id]");
                const kind = getListKind(dropArea);

                try {
                    if (targetItem && root.contains(targetItem)) {
                        const targetId = parseId(targetItem.dataset.menuId);
                        if (targetId && targetId !== draggedId) {
                            const rect = targetItem.getBoundingClientRect();
                            const placeAfter = event.clientY > rect.top + rect.height / 2;
                            const parentId = kind === "children" ? getParentId(dropArea.closest("[data-parent-id]") ?? dropArea) : null;
                            await instance.dotNetReference.invokeMethodAsync("ReorderDraggedMenuAsync", draggedId, targetId, parentId, placeAfter);
                        }
                    } else if (kind === "children") {
                        const parentId = getParentId(dropArea.closest("[data-parent-id]") ?? dropArea);
                        if (parentId && parentId !== draggedId) {
                            await instance.dotNetReference.invokeMethodAsync("MoveDraggedMenuToParentAsync", draggedId, parentId);
                        }
                    } else if (kind === "root") {
                        await instance.dotNetReference.invokeMethodAsync("MoveDraggedMenuToRootAsync", draggedId);
                    }
                } finally {
                    instance.draggedId = null;
                    clearDropState(root);
                }
            };

            instance.dragEnd = function () {
                instance.draggedId = null;
                clearDropState(root);
            };

            root.addEventListener("dragstart", instance.dragStart);
            root.addEventListener("dragover", instance.dragOver);
            root.addEventListener("drop", instance.drop);
            root.addEventListener("dragend", instance.dragEnd);
            instances.set(rootId, instance);
        },

        dispose: function (rootId) {
            const root = document.getElementById(rootId);
            const instance = instances.get(rootId);
            if (!root || !instance) {
                return;
            }

            root.removeEventListener("dragstart", instance.dragStart);
            root.removeEventListener("dragover", instance.dragOver);
            root.removeEventListener("drop", instance.drop);
            root.removeEventListener("dragend", instance.dragEnd);
            instances.delete(rootId);
        }
    };
})();
