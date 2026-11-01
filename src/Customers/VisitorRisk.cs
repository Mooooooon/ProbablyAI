using System;
using static ProbablyAI.Localization.Text;

namespace ProbablyAI.Customers
{
    // On-duty security staff created for inspections, fines, supply and story visits.
    // Named corrupt officers such as officerLun keep their own wanted flags.
    public static class VisitorRisk
    {
        public static bool IsOnDutySecurity(string identifier, bool isSecurity, bool isSentenceOfficer)
        {
            if (isSentenceOfficer) return true;
            if (isSecurity) return !IsKnownCorruptOfficer(identifier);
            // The morning leader is created without isSecurity; keep a name fallback for future staff clients.
            return identifier == "morningSecLeader" || identifier == "securityOfficer" ||
                   (identifier != null && identifier.StartsWith("securityOfficer", StringComparison.Ordinal)) ||
                   identifier == "securityInspector" || identifier == "patrolOfficer";
        }

        public static bool IsKnownCorruptOfficer(string identifier)
            => !string.IsNullOrEmpty(identifier) && identifier.StartsWith("officerLun", StringComparison.Ordinal);

        public static string Compose(bool wanted, bool aug)
        {
            if (wanted && aug) return T("（小声）主人，这个人符合通缉令上的特征，而且好像是改造体呢。");
            if (wanted) return T("（小声）主人，这个人符合通缉令上的特征呢。");
            if (aug) return T("（小声）主人，这个人好像是改造体呢。");
            return null;
        }

        // A local, deterministic notice: one per visit and never for on-duty security staff.
        public static string Decide(CustomerVisit visit, string lastNotifiedVisitId)
        {
            if (visit == null || visit.Status == "absent" || string.IsNullOrEmpty(visit.Id)) return null;
            if (visit.Id == lastNotifiedVisitId) return null;
            if (visit.OnDutySecurity) return null;
            return Compose(visit.Wanted, visit.Aug);
        }
    }
}
