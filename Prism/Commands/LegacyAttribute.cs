using System;

namespace Prism.Commands;

/// <summary>
/// Marks a command as retired from the interface. A legacy command is no longer offered in the ribbon, search or ribbon customization,
/// but its code stays so saved files that used it still reopen and re-edit, and scripts can still run it. Keeping the attribute on the
/// class makes the retired code easy to list (search for <c>[Legacy</c>) when deciding whether to delete it for good.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LegacyAttribute(string reason = "") : Attribute
{
    /// <summary>Why the command was retired and what to use instead.</summary>
    public string Reason { get; } = reason;

    public static bool IsLegacy(Type type) => Attribute.IsDefined(type, typeof(LegacyAttribute), false);
}
