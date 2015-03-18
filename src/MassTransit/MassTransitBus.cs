// Copyright 2007-2015 Chris Patterson, Dru Sellers, Travis Smith, et. al.
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
namespace MassTransit
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Context;
    using Pipeline;
    using Transports;


    public class MassTransitBus :
        IBusControl
    {
        readonly Uri _address;
        readonly IConsumePipe _consumePipe;
        readonly IBusHost[] _hosts;
        readonly IPublishEndpoint _publishEndpoint;
        readonly IReceiveEndpoint[] _receiveEndpoints;
        readonly ISendEndpointProvider _sendEndpointProvider;

        public MassTransitBus(Uri address, IConsumePipe consumePipe, ISendEndpointProvider sendEndpointProvider,
            IPublishEndpoint publishEndpoint, IEnumerable<IReceiveEndpoint> receiveEndpoints, IEnumerable<IBusHost> hosts)
        {
            _address = address;
            _consumePipe = consumePipe;
            _sendEndpointProvider = sendEndpointProvider;
            _publishEndpoint = publishEndpoint;
            _receiveEndpoints = receiveEndpoints.ToArray();
            _hosts = hosts.ToArray();
        }

        ConnectHandle IConsumePipeConnector.ConnectConsumePipe<T>(IPipe<ConsumeContext<T>> pipe)
        {
            return _consumePipe.ConnectConsumePipe(pipe);
        }

        ConnectHandle IRequestPipeConnector.ConnectRequestPipe<T>(Guid requestId, IPipe<ConsumeContext<T>> pipe)
        {
            return _consumePipe.ConnectRequestPipe(requestId, pipe);
        }

        ConnectHandle IConsumeMessageObserverConnector.ConnectConsumeMessageObserver<T>(IConsumeMessageObserver<T> observer)
        {
            return _consumePipe.ConnectConsumeMessageObserver(observer);
        }

        ConnectHandle IConsumeObserverConnector.ConnectConsumeObserver(IConsumeObserver observer)
        {
            return _consumePipe.ConnectConsumeObserver(observer);
        }

        Task IPublishEndpoint.Publish<T>(T message, CancellationToken cancellationToken)
        {
            return _publishEndpoint.Publish(message, cancellationToken);
        }

        Task IPublishEndpoint.Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken)
        {
            return _publishEndpoint.Publish(message, publishPipe, cancellationToken);
        }

        Task IPublishEndpoint.Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken)
        {
            return _publishEndpoint.Publish(message, publishPipe, cancellationToken);
        }

        Task IPublishEndpoint.Publish(object message, CancellationToken cancellationToken)
        {
            return _publishEndpoint.Publish(message, cancellationToken);
        }

        Task IPublishEndpoint.Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken)
        {
            return _publishEndpoint.Publish(message, publishPipe, cancellationToken);
        }

        Task IPublishEndpoint.Publish(object message, Type messageType, CancellationToken cancellationToken)
        {
            return PublishEndpointConverterCache.Publish(this, message, messageType, cancellationToken);
        }

        Task IPublishEndpoint.Publish(object message, Type messageType, IPipe<PublishContext> publishPipe,
            CancellationToken cancellationToken)
        {
            return PublishEndpointConverterCache.Publish(this, message, messageType, publishPipe, cancellationToken);
        }

        Task IPublishEndpoint.Publish<T>(object values, CancellationToken cancellationToken)
        {
            return _publishEndpoint.Publish<T>(values, cancellationToken);
        }

        Task IPublishEndpoint.Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken)
        {
            return _publishEndpoint.Publish(values, publishPipe, cancellationToken);
        }

        Task IPublishEndpoint.Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken)
        {
            return _publishEndpoint.Publish<T>(values, publishPipe, cancellationToken);
        }

        public Uri Address
        {
            get { return _address; }
        }

        Task<ISendEndpoint> ISendEndpointProvider.GetSendEndpoint(Uri address)
        {
            return _sendEndpointProvider.GetSendEndpoint(address);
        }

        async Task<BusHandle> IBusControl.Start(CancellationToken cancellationToken)
        {
            var receiveEndpointHandles = new List<ReceiveEndpointHandle>();

            Exception exception = null;
            try
            {
                Task<ReceiveEndpointHandle>[] receiveEndpoints = _receiveEndpoints.Select(x => x.Start(cancellationToken)).ToArray();
                foreach (var endpoint in receiveEndpoints)
                {
                    try
                    {
                        ReceiveEndpointHandle handle = await endpoint;

                        receiveEndpointHandles.Add(handle);
                    }
                    catch (Exception ex)
                    {
                        exception = ex;
                    }
                }
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            if (exception != null)
            {
                await Task.WhenAll(receiveEndpointHandles.Select(x => x.Stop(cancellationToken)));

                throw new MassTransitException("The service bus could not be started.", exception);
            }

            return new Handle(receiveEndpointHandles.ToArray(), _hosts);
        }


        class Handle :
            BusHandle
        {
            readonly IBusHost[] _hosts;
            readonly ReceiveEndpointHandle[] _receiveEndpoints;

            public Handle(ReceiveEndpointHandle[] receiveEndpoints, IBusHost[] hosts)
            {
                _receiveEndpoints = receiveEndpoints;
                _hosts = hosts;
            }

            public void Dispose()
            {
                Stop(default(CancellationToken)).Wait();
            }

            public async Task Stop(CancellationToken cancellationToken)
            {
                await Task.WhenAll(_receiveEndpoints.Select(x => x.Stop(cancellationToken)));
                await Task.WhenAll(_hosts.Select(x => x.Close(cancellationToken)));
            }
        }
    }
}