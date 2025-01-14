// <auto-generated/>
#pragma warning disable
using Microsoft.Extensions.Logging;
using Wolverine.Marten.Publishing;

namespace Internal.Generated.WolverineHandlers
{
    // START: ReservationTimeoutHandler457905910
    public class ReservationTimeoutHandler457905910 : Wolverine.Runtime.Handlers.MessageHandler
    {
        private readonly Microsoft.Extensions.Logging.ILogger<WolverineWebApi.Reservation> _logger;
        private readonly Wolverine.Marten.Publishing.OutboxedSessionFactory _outboxedSessionFactory;

        public ReservationTimeoutHandler457905910(Microsoft.Extensions.Logging.ILogger<WolverineWebApi.Reservation> logger, Wolverine.Marten.Publishing.OutboxedSessionFactory outboxedSessionFactory)
        {
            _logger = logger;
            _outboxedSessionFactory = outboxedSessionFactory;
        }



        public override async System.Threading.Tasks.Task HandleAsync(Wolverine.Runtime.MessageContext context, System.Threading.CancellationToken cancellation)
        {
            var reservationTimeout = (WolverineWebApi.ReservationTimeout)context.Envelope.Message;
            await using var documentSession = _outboxedSessionFactory.OpenSession(context);
            string sagaId = context.Envelope.SagaId ?? reservationTimeout.Id;
            if (string.IsNullOrEmpty(sagaId)) throw new Wolverine.Persistence.Sagas.IndeterminateSagaStateIdException(context.Envelope);
            
            // Try to load the existing saga document
            var reservation = await documentSession.LoadAsync<WolverineWebApi.Reservation>(sagaId, cancellation).ConfigureAwait(false);
            if (reservation == null)
            {
                return;
            }

            else
            {
                reservation.Handle(reservationTimeout, _logger);
                if (reservation.IsCompleted())
                {
                    
                    // Register the document operation with the current session
                    documentSession.Delete(reservation);
                }

                else
                {
                    
                    // Register the document operation with the current session
                    documentSession.Update(reservation);
                }

                
                // Commit all pending changes
                await documentSession.SaveChangesAsync(cancellation).ConfigureAwait(false);

            }

        }

    }

    // END: ReservationTimeoutHandler457905910
    
    
}

