namespace FHT.Access.Application.Services;

/// <summary>
/// Escolhe a webcam da entrada sem depender da porta USB.
/// A de saída não é aberta quando outra câmera responde.
/// </summary>
public static class EntryCameraSelection
{
    public static int Choose(int preferredIndex, int exitIndex, IReadOnlyList<int> workingIndices)
    {
        if (workingIndices.Count == 0)
            return preferredIndex >= 0 ? preferredIndex : 0;

        if (workingIndices.Count == 1)
            return workingIndices[0];

        if (preferredIndex >= 0
            && preferredIndex != exitIndex
            && workingIndices.Contains(preferredIndex))
            return preferredIndex;

        foreach (var index in workingIndices)
        {
            if (index != exitIndex)
                return index;
        }

        return workingIndices[0];
    }

    /// <summary>
    /// Ordem para abrir de verdade, sem sondar antes.
    /// A porta salva vem primeiro. A da saída só entra por último.
    /// </summary>
    public static IReadOnlyList<int> TryOrder(int preferredIndex, int exitIndex, int maxIndex = 5)
    {
        var order = new List<int>();

        void Add(int index)
        {
            if (index < 0 || index > maxIndex || order.Contains(index))
                return;
            order.Add(index);
        }

        if (preferredIndex != exitIndex)
            Add(preferredIndex);

        for (var index = 0; index <= maxIndex; index++)
        {
            if (index != exitIndex)
                Add(index);
        }

        Add(exitIndex);

        if (order.Count == 0)
            order.Add(0);

        return order;
    }
}
