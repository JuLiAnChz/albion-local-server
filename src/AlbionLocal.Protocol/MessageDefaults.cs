using System.Reflection;

namespace AlbionLocal.Server.Protocol;

public static class MessageDefaults
{
    /// <summary>
    /// Replaces null strings, arrays and dictionaries in [Param] fields with empty values.
    /// The client indexes many response arrays without null checks (e.g. the Login character
    /// list in LoginInProcessDialog.CreateCharacterRecord), so a missing array is a crash.
    /// </summary>
    public static T FillEmpty<T>(this T msg) where T : class
    {
        foreach (var f in msg.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.GetCustomAttribute<ParamAttribute>() is null || f.GetValue(msg) is not null) continue;
            var t = f.FieldType;
            if (t == typeof(string)) f.SetValue(msg, "");
            else if (t.IsArray) f.SetValue(msg, Array.CreateInstance(t.GetElementType()!, 0));
            else if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                f.SetValue(msg, Activator.CreateInstance(t));
        }
        return msg;
    }
}
