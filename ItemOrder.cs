namespace FloatingPhrases;

public static class ItemOrder
{
    public static bool Move<T>(IList<T> items, T item, T target, bool insertAfter)
    {
        var sourceIndex = items.IndexOf(item);
        var targetIndex = items.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return false;
        }

        items.RemoveAt(sourceIndex);
        targetIndex = items.IndexOf(target);
        var insertIndex = insertAfter ? targetIndex + 1 : targetIndex;
        items.Insert(Math.Clamp(insertIndex, 0, items.Count), item);
        return sourceIndex != insertIndex;
    }
}
