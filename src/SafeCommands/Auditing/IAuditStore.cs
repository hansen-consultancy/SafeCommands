namespace SafeCommands.Auditing;

interface IAuditStore
{
    void Append(AuditRecord record);
}
