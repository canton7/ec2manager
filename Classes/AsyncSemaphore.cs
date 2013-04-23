using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ec2Manager.Classes
{
    public class AsyncSemaphore
    {
        private readonly static Task completed = Task.FromResult(true);
        private readonly Queue<TaskCompletionSource<bool>> waiters = new Queue<TaskCompletionSource<bool>>();
        private readonly object waitersLockObject = new object();
        private int currentCount;
        private int maxCount;

        public AsyncSemaphore(int initialCount, int maxCount = int.MaxValue)
        {
            if (initialCount < 0)
            {
                throw new ArgumentOutOfRangeException("initialCount");
            }

            this.currentCount = initialCount;
            this.maxCount = maxCount;
        }

        public Task WaitAsync()
        {
            lock (this.waitersLockObject)
            {
                if (this.currentCount > 0)
                {
                    this.currentCount--;
                    return completed;
                }
                else
                {
                    var waiter = new TaskCompletionSource<bool>();

                    this.waiters.Enqueue(waiter);

                    return waiter.Task;
                }
            }
        }

        public Task<bool> TryWaitAsync()
        {
            lock (this.waitersLockObject)
            {
                if (this.currentCount > 0)
                {
                    this.currentCount--;
                    return Task.FromResult(true);
                }
                else
                {
                    return Task.FromResult(false);
                }
            }
        }

        public void Release()
        {
            TaskCompletionSource<bool> toRelease = null;

            lock (this.waitersLockObject)
            {
                if (this.waiters.Count > 0)
                {
                    toRelease = waiters.Dequeue();
                }
                else if (this.currentCount < this.maxCount)
                {
                    this.currentCount++;
                }
            }

            if (toRelease != null)
            {
                Task.Run(() => toRelease.SetResult(true));
            }
        }
    }
}
