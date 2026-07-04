namespace MediaInfoKeeper.Services
{
    internal static class RangeCachePrewarmTriggerPolicy
    {
        public static bool ShouldTriggerAfterItemAdded(bool hasMediaInfo, bool restoredMediaInfo)
        {
            return hasMediaInfo || restoredMediaInfo;
        }
    }
}
