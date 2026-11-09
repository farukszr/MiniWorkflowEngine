using Microsoft.AspNetCore.Mvc.Rendering;

namespace MiniWorkflowEngine.ViewModels;

public sealed record DepartmentListItem(int Id, string Name, int MemberCount);
public sealed record DepartmentMember(string Id, string Name, string? Email, int? DepartmentId);
public sealed record DepartmentsViewModel(IReadOnlyList<DepartmentListItem> Departments,
    IReadOnlyList<DepartmentMember> Members, IReadOnlyList<SelectListItem> DepartmentOptions);
