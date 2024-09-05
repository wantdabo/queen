
using Queen.Server.Core;

namespace Queen.Server.Roles.Common.Contacts;

public class Contact : Comp
{
    private Role role { get; set; }
    private Pager pager { get; set; }

    public void Initialize(Role role)
    {
        this.role = role;
    }

    protected override void OnCreate()
    {
        base.OnCreate();
        pager = AddComp<Pager>();
        pager.Initialize(role);
        pager.Create();
    }

    public bool Search(string objective, out Pager pager)
    {
        this.pager.ChangeObjective(objective);
        pager = this.pager;

        return true;
    }
}
