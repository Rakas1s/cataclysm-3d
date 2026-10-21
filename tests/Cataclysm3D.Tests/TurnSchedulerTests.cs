using Cataclysm3D.Sim;

namespace Cataclysm3D.Tests;

public class TurnSchedulerTests
{
    private sealed class CountingActor : Actor
    {
        public int Actions { get; private set; }
        public CountingActor(int speed) : base("counter") => Speed = speed;

        public override void Act(TurnScheduler scheduler)
        {
            Actions++;
            Moves -= 100;
        }
    }

    [Fact]
    public void OneFullSpeedActionTakesOneTurn()
    {
        var sim = new TurnScheduler(new PlayerActor());
        sim.SubmitPlayerAction(100);
        Assert.Equal(1, sim.Turn);
        Assert.True(sim.PlayerCanAct);
    }

    [Fact]
    public void CheapActionsDoNotAdvanceTime()
    {
        var sim = new TurnScheduler(new PlayerActor());
        sim.SubmitPlayerAction(50);
        Assert.Equal(0, sim.Turn);
        sim.SubmitPlayerAction(50);
        Assert.Equal(1, sim.Turn);
    }

    [Fact]
    public void SlowActorActsProportionallyLess()
    {
        var sim = new TurnScheduler(new PlayerActor());
        var zombie = new CountingActor(speed: 70);
        sim.Add(zombie);

        for (int i = 0; i < 10; i++)
            sim.SubmitPlayerAction(100);

        Assert.Equal(10, sim.Turn);
        Assert.InRange(zombie.Actions, 7, 8);
    }

    [Fact]
    public void ActorThatSpendsNoMovesIsRejected()
    {
        var sim = new TurnScheduler(new PlayerActor());
        sim.Add(new LazyActor());
        Assert.Throws<InvalidOperationException>(() => sim.SubmitPlayerAction(100));
    }

    private sealed class LazyActor : Actor
    {
        public LazyActor() : base("lazy") { }
        public override void Act(TurnScheduler scheduler) { }
    }
}
