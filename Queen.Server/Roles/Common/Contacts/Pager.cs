using Queen.Core;

namespace Queen.Server.Roles.Common.Contacts;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class RoleContact : Attribute
{
}

/// <summary>
/// 传呼机, Role 与 Role 之间的沟通
/// </summary>
public class Pager : Comp
{
    public Role role { get; private set; }
    public string objective { get; private set; }

    public void Initialize(Role role)
    {
        this.role = role;
    }

    public void ChangeObjective(string objective)
    {
        this.objective = objective;
    }
}
