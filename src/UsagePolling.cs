using System;
using System.Collections.Generic;

namespace CodexRelay {
    internal sealed class UsagePolling {
        readonly Dictionary<string,long> checkedAt=new Dictionary<string,long>();
        internal static bool NearLimit(Usage usage) {
            return usage!=null && ((usage.FiveHour!=null && usage.FiveHour.Remaining<=5) || (usage.Weekly!=null && usage.Weekly.Remaining<=5));
        }
        internal static int Interval(Account account,bool active) {
            if(!active)return 90;
            if(account.Usage!=null&&!string.IsNullOrEmpty(account.Usage.Error))return 30;
            return NearLimit(account.Usage)?5:10;
        }
        internal bool Due(Account account,bool active,long now,bool force) {
            if(account==null||account.NeedsLogin)return false;
            long last;
            if(!checkedAt.TryGetValue(account.Id,out last))last=account.Usage==null?0:account.Usage.FetchedAt;
            return force || account.Usage==null || last==0 || now<last || now-last>=Interval(account,active);
        }
        internal void Record(Account account,long now) { checkedAt[account.Id]=now; }
    }
}
