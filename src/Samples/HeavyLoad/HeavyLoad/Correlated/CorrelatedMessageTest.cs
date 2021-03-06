// Copyright 2007-2008 The Apache Software Foundation.
//  
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use 
// this file except in compliance with the License. You may obtain a copy of the 
// License at 
// 
//     http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software distributed 
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
// CONDITIONS OF ANY KIND, either express or implied. See the License for the 
// specific language governing permissions and limitations under the License.
namespace HeavyLoad.Correlated
{
	using System;
	using System.Threading;
	using Castle.Windsor;
	using MassTransit;
	using MassTransit.Transports.Msmq;

    public class CorrelatedMessageTest : IDisposable
	{
		private readonly int _attempts = 5000;
		private readonly IWindsorContainer _container;
		private readonly ManualResetEvent _finishedEvent = new ManualResetEvent(false);
		private readonly IServiceBus _bus;
		private int _successes;
		private int _timeouts;
    	private UnsubscribeAction _unsubscribeToken;

    	public CorrelatedMessageTest()
		{
			_container = new HeavyLoadContainer();

			_bus = _container.Resolve<IServiceBus>();

			var management = MsmqEndpointManagement.New(_bus.Endpoint.Address.Uri);
			management.Purge();
		}

		public void Dispose()
		{
			_bus.Dispose();
			_container.Dispose();
		}


		public void Run(StopWatch stopWatch)
		{
			stopWatch.Start();

			SimpleRequestService service = new SimpleRequestService(_bus);

			_unsubscribeToken = _bus.Subscribe(service);

			CheckPoint point = stopWatch.Mark("Correlated Requests");
		    CheckPoint responsePoint = stopWatch.Mark("Correlated Responses");

		    ManagedThreadPool<CorrelatedController> pool = new ManagedThreadPool<CorrelatedController>(DoWorker, 10, 10);
            try
            {

                for (int index = 0; index < _attempts; index++)
                {
                    CorrelatedController controller = new CorrelatedController(_bus, OnSuccess, OnTimeout);
                    pool.Enqueue(controller);
                }

                point.Complete(_attempts);

                _finishedEvent.WaitOne(TimeSpan.FromSeconds(60), true);

                responsePoint.Complete(_timeouts + _successes);
            }
            finally
            {
                pool.Dispose();
            }

			_unsubscribeToken();

			Console.WriteLine("Attempts: {0}, Succeeded: {1}, Timeouts: {2}", _attempts, _successes, _timeouts);

			stopWatch.Stop();
		}

        private void DoWorker(CorrelatedController controller)
        {
            controller.SimulateRequestResponse();
        }

        private void OnTimeout(CorrelatedController obj)
		{
			lock (_finishedEvent)
			{
				Interlocked.Increment(ref _timeouts);

				if (_timeouts + _successes == _attempts)
					_finishedEvent.Set();
			}
		}

        private void OnSuccess(CorrelatedController obj)
		{
			lock (_finishedEvent)
			{
				Interlocked.Increment(ref _successes);

				if (_timeouts + _successes == _attempts)
					_finishedEvent.Set();
			}
		}
	}
}