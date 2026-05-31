using System;

namespace AllTimeSoundTrigger.Core;

public sealed class EventBus
{
    private readonly object gate = new();
    private Action<GameEvent>? handlers;

    public IDisposable Subscribe(Action<GameEvent> handler)
    {
        lock (gate)
            handlers += handler;

        return new Subscription(this, handler);
    }

    public void Publish(GameEvent gameEvent)
    {
        Action<GameEvent>? snapshot;
        lock (gate)
            snapshot = handlers;

        snapshot?.Invoke(gameEvent);
    }

    private void Unsubscribe(Action<GameEvent> handler)
    {
        lock (gate)
            handlers -= handler;
    }

    private sealed class Subscription(EventBus owner, Action<GameEvent> handler) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            owner.Unsubscribe(handler);
        }
    }
}
