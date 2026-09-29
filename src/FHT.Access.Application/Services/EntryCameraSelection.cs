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
}
