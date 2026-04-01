namespace JortPob.Common;

using System;

[AttributeUsage(AttributeTargets.Property)]
public class SettingAttribute : Attribute
{
    public bool IsRequired { get; }
    public object DefaultValue { get; }

    // No arguments = REQUIRED
    public SettingAttribute()
    {
        IsRequired = true;
    }

    // ANY arguments = OPTIONAL (Uses params to capture single items or comma-separated arrays)
    public SettingAttribute(params object[] values)
    {
        IsRequired = false;
        
        // Support for [Setting(null)]
        if (values == null)
        {
            DefaultValue = null;
        }
        else
        {
            // If one item, store it. If multiple, store the array.
            DefaultValue = values.Length == 1 ? values[0] : values;
        }
    }
}