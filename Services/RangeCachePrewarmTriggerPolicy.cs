namespace MediaInfoKeeper.Services
{
    internal static class RangeCachePrewarmTriggerPolicy
    {
        public static bool ShouldTriggerAfterItemAdded(
            bool hasMediaInfo,
            bool restoredMediaInfo,
            bool extractedMediaInfo = false)
        {
            return hasMediaInfo || restoredMediaInfo || extractedMediaInfo;
        }
    }
}
