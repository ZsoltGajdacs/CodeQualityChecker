namespace Cqc.Java;

internal static class JvmDescriptor
{
    private static readonly Dictionary<char, string> Primitives = new()
    { ['Z'] = "boolean", ['B'] = "byte", ['C'] = "char", ['S'] = "short", ['I'] = "int", ['J'] = "long", ['F'] = "float", ['D'] = "double" };

    public static string[] Parameters(string descriptor)
    {
        var result = new List<string>();
        var index = descriptor.IndexOf('(') + 1;
        if (index == 0) throw new InvalidDataException("Invalid JVM method descriptor.");
        while (HasParameter(descriptor, index)) result.Add(ReadType(descriptor, ref index));
        return result.ToArray();
    }

    private static bool HasParameter(string descriptor, int index) => index < descriptor.Length && descriptor[index] != ')';

    private static string ReadType(string descriptor, ref int index)
    {
        var arrays = 0;
        while (descriptor[index] == '[') { arrays++; index++; }
        var code = descriptor[index++];
        var type = code == 'L' ? ReadObject(descriptor, ref index) : Primitives.GetValueOrDefault(code)
            ?? throw new InvalidDataException("Invalid JVM descriptor.");
        return type + string.Concat(Enumerable.Repeat("[]", arrays));
    }

    private static string ReadObject(string descriptor, ref int index)
    {
        var end = descriptor.IndexOf(';', index);
        if (end < 0) throw new InvalidDataException("Invalid JVM object descriptor.");
        var type = descriptor[index..end].Replace('/', '.');
        index = end + 1;
        return type;
    }
}
