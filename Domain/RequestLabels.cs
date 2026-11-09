using MiniWorkflowEngine.Domain.Enums;

namespace MiniWorkflowEngine.Domain;

public static class RequestLabels
{
    public static string Status(RequestStatus status) => status switch
    {
        RequestStatus.Draft => "Taslak", RequestStatus.Pending => "Onay bekliyor", RequestStatus.Approved => "Onaylandı",
        RequestStatus.Rejected => "Reddedildi", RequestStatus.Cancelled => "İptal edildi", _ => "Bilinmeyen"
    };
    public static string Action(RequestActionType action) => action switch
    {
        RequestActionType.DraftCreated => "Taslak oluşturuldu", RequestActionType.DraftUpdated => "Taslak güncellendi",
        RequestActionType.Submitted => "Onaya gönderildi", RequestActionType.Approved => "Adım onaylandı",
        RequestActionType.Rejected => "Talep reddedildi", RequestActionType.Cancelled => "Talep iptal edildi",
        RequestActionType.AdministratorApproved => "Yönetici adımı onayladı",
        RequestActionType.AdministratorRejected => "Yönetici talebi reddetti",
        RequestActionType.StepSkipped => "Adım otomatik atlandı", RequestActionType.Completed => "Süreç tamamlandı", _ => "İşlem"
    };
    public static string StatusClass(RequestStatus status) => status switch
    {
        RequestStatus.Pending => "status-pending", RequestStatus.Approved => "status-approved",
        RequestStatus.Rejected => "status-rejected", _ => "status-neutral"
    };
}
