using Xunit;

namespace NewsBoard.Tests;

/// <summary>
/// 播放引擎相关测试共享一个 collection：它们用真实（缩放）时钟驱动状态机，
/// 并发执行会互相抢 CPU 让时间断言抖动。放在同一 collection 里串行跑。
/// </summary>
[CollectionDefinition(PlaybackCollection.Name, DisableParallelization = true)]
public sealed class PlaybackCollection
{
    public const string Name = "Playback";
}
