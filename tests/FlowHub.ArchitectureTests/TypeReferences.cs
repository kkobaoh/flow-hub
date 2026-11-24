using System.Reflection;
using System.Reflection.Emit;

namespace FlowHub.ArchitectureTests;

/// <summary>
/// 型が使っている型と、呼び出しているメソッドを集める。シグネチャ（基底、インターフェース、フィールド、メソッドの引数と戻り値）に加えて、
/// メソッドの中身（IL）で使っている型とメソッドも集める（中身の中だけで使っている依存も見逃さないため）。
/// </summary>
internal static class TypeReferences
{
    private const BindingFlags AllMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opCode => opCode.Value);

    /// <summary>型が使っている型（ジェネリックの引数や配列の要素も、ばらして含める）。</summary>
    public static IReadOnlySet<Type> UsedTypes(Type type)
    {
        var used = new HashSet<Type>();
        AddType(used, type.BaseType);
        foreach (var implemented in type.GetInterfaces())
        {
            AddType(used, implemented);
        }

        foreach (var field in type.GetFields(AllMembers))
        {
            AddType(used, field.FieldType);
        }

        foreach (var method in Methods(type))
        {
            if (method is MethodInfo info)
            {
                AddType(used, info.ReturnType);
            }

            foreach (var parameter in method.GetParameters())
            {
                AddType(used, parameter.ParameterType);
            }

            foreach (var member in MembersUsedIn(method))
            {
                switch (member)
                {
                    case Type usedType:
                        AddType(used, usedType);
                        break;
                    case FieldInfo field:
                        AddType(used, field.DeclaringType);
                        AddType(used, field.FieldType);
                        break;
                    case MethodBase called:
                        AddType(used, called.DeclaringType);
                        break;
                }
            }
        }

        return used;
    }

    /// <summary>型のメソッドの中身で呼び出しているメソッド（コンストラクターも含む）。</summary>
    public static IEnumerable<(MethodBase Caller, MethodBase Called)> CalledMethods(Type type) =>
        Methods(type).SelectMany(method => MembersUsedIn(method).OfType<MethodBase>().Select(called => (method, called)));

    private static IEnumerable<MethodBase> Methods(Type type) =>
        type.GetMethods(AllMembers).Cast<MethodBase>().Concat(type.GetConstructors(AllMembers));

    private static void AddType(HashSet<Type> used, Type? type)
    {
        if (type is null || !used.Add(type))
        {
            return;
        }

        if (type.HasElementType)
        {
            AddType(used, type.GetElementType());
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                AddType(used, argument);
            }
        }
    }

    /// <summary>
    /// メソッドの中身（IL）が使っている型、フィールド、メソッド。
    /// </summary>
    private static List<MemberInfo> MembersUsedIn(MethodBase method)
    {
        var members = new List<MemberInfo>();
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            return members;
        }

        var typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        var position = 0;
        while (position < il.Length)
        {
            var value = il[position] == 0xFE ? (short)(0xFE00 | il[position + 1]) : il[position];
            position += il[position] == 0xFE ? 2 : 1;
            var opCode = OpCodesByValue[value];

            if (opCode.OperandType is OperandType.InlineType or OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineTok)
            {
                var token = BitConverter.ToInt32(il, position);
                try
                {
                    if (method.Module.ResolveMember(token, typeArguments, methodArguments) is { } member)
                    {
                        members.Add(member);
                    }
                }
                catch (ArgumentException)
                {
                    // 解決できない（読み込んでいないアセンブリなど）ものは、FlowHub の型ではないので無視する
                }
            }

            position += OperandSize(opCode, il, position);
        }

        return members;
    }

    private static int OperandSize(OpCode opCode, byte[] il, int position) => opCode.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (BitConverter.ToInt32(il, position) * 4),
        _ => 4,
    };
}
