using System.Text.RegularExpressions;
using SpinUp.Api.Contracts;

namespace SpinUp.Api.Validation;

public static class ServiceRequestValidator
{
    private static readonly Regex WindowsPathPattern = new(@"^[a-zA-Z]:\\", RegexOptions.Compiled);

    public static IDictionary<string, string[]> Validate(ServiceCreateRequest request)
    {
        return ValidateCommon(request.Name, request.Path, request.Command, request.Args, request.Env);
    }

    public static IDictionary<string, string[]> Validate(ServiceUpdateRequest request)
    {
        return ValidateCommon(request.Name, request.Path, request.Command, request.Args, request.Env);
    }

    private static IDictionary<string, string[]> ValidateCommon(
        string name,
        string path,
        string command,
        string? args,
        Dictionary<string, string>? env)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        ValidateRequired(name, "name", 100, errors);
        ValidateRequired(path, "path", 260, errors);
        ValidateRequired(command, "command", 200, errors);

        if (!string.IsNullOrWhiteSpace(path) && !WindowsPathPattern.IsMatch(path.Trim()))
        {
            AddError(errors, "path", "Path must be an absolute Windows path (for example, C:\\projects\\erpcom).");
        }

        if (!string.IsNullOrWhiteSpace(args) && args.Length > 500)
        {
            AddError(errors, "args", "Args must be 500 characters or fewer.");
        }

        if (env is not null && env.Any(x => string.IsNullOrWhiteSpace(x.Key)))
        {
            AddError(errors, "env", "Environment variable keys cannot be empty.");
        }

        return errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    private static void ValidateRequired(string value, string field, int maxLength, IDictionary<string, List<string>> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, field, $"{field} is required.");
            return;
        }

        if (value.Length > maxLength)
        {
            AddError(errors, field, $"{field} must be {maxLength} characters or fewer.");
        }
    }

    private static void AddError(IDictionary<string, List<string>> errors, string key, string value)
    {
        if (!errors.TryGetValue(key, out var list))
        {
            list = new List<string>();
            errors[key] = list;
        }

        list.Add(value);
    }
}
