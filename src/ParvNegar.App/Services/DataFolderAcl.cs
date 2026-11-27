using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ParvNegar.App.Services
{
    /// <summary>
    /// Data-folder ACL policy (SESSION-SECURITY.md §7). When ParvNegar creates its data root, the folder gets
    /// a protected DACL (no inheritance from %ProgramData%, where every local user may read):
    ///   SYSTEM: Full control · Administrators: Full control · the Windows account that created it: Full control.
    /// Other Windows accounts of a shared school PC are added by an administrator when the owner decides
    /// (decision O-ACL), for example: icacls "%ProgramData%\OARNOVA\ParvNegar" /grant "PC\Secretary:(OI)(CI)M".
    /// An existing folder is never changed (its ACL may have been set by an administrator on purpose).
    /// </summary>
    public static class DataFolderAcl
    {
        public static bool EnsureCreated(string root, out string detail)
        {
            if (Directory.Exists(root)) { detail = "existing folder left unchanged"; return false; }
            Directory.CreateDirectory(root);
            try
            {
                var sec = new DirectorySecurity();
                sec.SetAccessRuleProtection(true, false);
                var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                using (var me = WindowsIdentity.GetCurrent())
                    sec.AddAccessRule(new FileSystemAccessRule(me.User, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(root).SetAccessControl(sec);
                detail = "protected ACL applied";
                return true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException || ex is PrivilegeNotHeldException || ex is InvalidOperationException)
            {
                detail = "ACL could not be applied: " + ex.GetType().Name;
                return false;
            }
        }
    }
}
