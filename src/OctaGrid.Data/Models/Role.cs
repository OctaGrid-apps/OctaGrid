namespace OctaGrid.Data.Models;

// Stored by name, so the order here is free. What each role may do is not decided here: see RolePermissions.
public enum Role
{
    Owner,
    Admin,
    Operator,
    Reader
}
