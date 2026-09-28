using System;
using System.Threading;
using System.Threading.Tasks;
using HostaApp.ControlSystem.ExtModules.TunableLaser;
using static HostaApp.ControlSystem.ExtModules.TunableLaser.Enums;
using Enum = System.Enum;

namespace ControlSystem
{
    /// <summary>
    /// State-machine driven module that owns a single <see cref="ILaserAdapter"/>
    /// and translates equipment-level events (init started, shutdown) and
    /// adapter results into laser lifecycle transitions
    /// (NotConnected → Connecting → Connected → Initializing → Idle).
    /// </summary>
    public class LaserHandler : StateMachine
    {
        #region State Machine

        /// <summary>Laser lifecycle states.</summary>
        public enum StateId
        {
            NotConnected,
            Connecting,
            Connected,
            Initializing,
            Idle
        }

        /// <summary>Triggers that drive transitions between <see cref="StateId"/>s.</summary>
        public enum TriggerId
        {
            TryConnect,
            ConnectSuccess,
            ConnectFailed,

            Initialize,
            InitSuccess,
            InitFailed,

            Disconnect
        }

        /// <summary>Events raised by the state machine after a transition completes.</summary>
        public enum EventId
        {
            Connecting,
            ConnectSuccess,
            ConnectFailed,

            Initializing,
            InitSuccess,
            InitFailed,

            Disconnected
        }

        #endregion
        private static readonly AlarmId[] LaserAlarms =
       {
            AlarmId.ERRLSR001,
            AlarmId.ERRLSR002,
        };

        private ILaserAdapter _laserAdapter;

        /*
         * Tracks whether the current Disconnect transition was
         * requested by the equipment (graceful shutdown) versus
         * caused by a link/controller failure. Uses Interlocked
         * to avoid races between overlapping shutdown + link-drop
         * events.
         */
        private int _shutdownRequested; // 0 = false, 1 = true

        /// <summary>
        /// Initializes a new <see cref="LaserHandler"/>, configures its state
        /// machine, selects the concrete laser adapter based on application
        /// settings, and publishes the instance on <see cref="HostInterface"/>.
        /// </summary>
        /// <param name="parent">Parent module in the equipment hierarchy.</param>
        public LaserHandler(ModuleObj parent)
            : base(parent)
        {
            ConfigureStateMachine();

            SetCurrentState(StateId.NotConnected.ToString());

            this.AddToInitialiseList(this, InitStateEnum.Unknown);

            var laserTypeValue =
                AppSettingsManager.SingleInstance.GetIntValue(
                    StringConstants.AppSettingKeys.TunableLaserSettings_LaserType);

            var laserType =
                Enum.IsDefined(typeof(LaserType), laserTypeValue)
                    ? (LaserType)laserTypeValue
                    : LaserType.TunableToptica;

            InitializeAdapter(laserType);

            // Publish only after fully constructed.
            HostInterface.SingleInstance.LaserHandlerPointer = this;
        }

        #region State Machine Configuration

        /// <summary>
        /// Registers every valid state/trigger/event tuple for the laser
        /// lifecycle, including the "disconnect from any state" fallbacks.
        /// </summary>
        private void ConfigureStateMachine()
        {
            AddValidStateChange(StateId.NotConnected, TriggerId.TryConnect,
                StateId.Connecting, EventId.Connecting);

            AddValidStateChange(StateId.Connecting, TriggerId.ConnectSuccess,
                StateId.Connected, EventId.ConnectSuccess);

            AddValidStateChange(StateId.Connecting, TriggerId.ConnectFailed,
                StateId.NotConnected, EventId.ConnectFailed);

            AddValidStateChange(StateId.Connected, TriggerId.Initialize,
                StateId.Initializing, EventId.Initializing);

            AddValidStateChange(StateId.Initializing, TriggerId.InitSuccess,
                StateId.Idle, EventId.InitSuccess);

            AddValidStateChange(StateId.Initializing, TriggerId.InitFailed,
                StateId.Connected, EventId.InitFailed);

            AddDisconnectFromAnyState();
        }

        /// <summary>
        /// Registers a <see cref="TriggerId.Disconnect"/> transition from every
        /// <see cref="StateId"/> back to <see cref="StateId.NotConnected"/>,
        /// so disconnect is always accepted regardless of the current state.
        /// </summary>
        private void AddDisconnectFromAnyState()
        {
            foreach (StateId s in Enum.GetValues(typeof(StateId)))
            {
                AddValidStateChange(s, TriggerId.Disconnect,
                    StateId.NotConnected, EventId.Disconnected);
            }
        }

        /// <summary>
        /// Clears all laser-related alarms .
        /// </summary>
        private void ResetAllAlarms()
        {
            foreach (var alarmId in LaserAlarms)
            {
                AlarmManager.SingleInstance.ResetAlarm(alarmId);
            }
        }

        /// <summary>
        /// Strongly-typed wrapper around the base
        /// <c>AddValidStateChange(string,string,string,string)</c>.
        /// </summary>
        /// <param name="currentState">Source state of the transition.</param>
        /// <param name="trigger">Trigger that causes the transition.</param>
        /// <param name="newState">Destination state after the transition.</param>
        /// <param name="eventId">Event raised once the transition completes.</param>
        private void AddValidStateChange(
            StateId currentState,
            TriggerId trigger,
            StateId newState,
            EventId eventId)
        {
            AddValidStateChange(
                currentState.ToString(),
                trigger.ToString(),
                newState.ToString(),
                eventId.ToString());
        }

        /// <summary>
        /// Issues the disconnect command to the adapter and logs the outcome.
        /// Never throws; safe to invoke fire-and-forget from
        /// <see cref="OnPostStateChange"/>.
        /// </summary>
        private void SafeDisconnect()
        {
            try
            {
                if (_laserAdapter == null) return;

                var result =
                     _laserAdapter.ExecuteCommandAndWait<bool>(
                        LaserWriteCommand.Disconnect);

                bool ok = result != null && result.IsSuccess;

                LogManager.SingleInstance.WriteLog(this,
                    ok
                        ? "Laser controller disconnected successfully."
                        : $"Error disconnecting laser controller: {result?.ErrorMessage}",
                    ok ? LoglevelEnum.Information : LoglevelEnum.Warning);
            }
            catch (Exception ex)
            {
                LogManager.SingleInstance.WriteLog(this,
                    $"Exception disconnecting laser controller: {ex.Message}",
                    LoglevelEnum.Error);
            }
        }

        #endregion

        #region Module Notifications

        /// <summary>
        /// Reacts to notifications from other modules. Only
        /// <see cref="EquipmentManager"/> messages are relevant here:
        /// <see cref="EquipmentManager.EventId.InitStarted"/> begins the
        /// connect sequence, <see cref="EquipmentManager.EventId.Shutdown"/>
        /// triggers a graceful disconnect.
        /// </summary>
        /// <param name="eventMsg">Incoming module event message.</param>
        protected override void OnModuleNotification(EventMessage eventMsg)
        {
            if (eventMsg.EventSource.GetType() != typeof(EquipmentManager))
            {
                return;
            }

            EquipmentManager.EventId eventId =
                (EquipmentManager.EventId)Enum.Parse(
                    typeof(EquipmentManager.EventId),
                    eventMsg.EventId);

            switch (eventId)
            {
                case EquipmentManager.EventId.InitStarted:
                    DoStateChange(TriggerId.TryConnect.ToString());
                    break;

                case EquipmentManager.EventId.Shutdown:
                    if (_laserAdapter != null)
                    {
                        Interlocked.Exchange(ref _shutdownRequested, 1);
                        DoStateChange(TriggerId.Disconnect.ToString());
                    }
                    break;
            }
        }

        #endregion

        #region State Change Handling
     
        protected override void OnPostStateChange(EventMessage eventMsg, StateChangeInfo stateChangeInfo)
        {
            EventId eventid = (EventId)System.Enum.Parse(typeof(EventId), eventMsg.EventId);

            switch (eventid)
            {
                case EventId.Connecting:
                    {
                        // Perform the actual controller connect on entering Connecting.
                        LaserCommandResult<bool> result =
                     _laserAdapter.ExecuteCommandAndWait<bool>(LaserWriteCommand.Connect);

                         if (!result.IsSuccess)
                        {
                            LogManager.SingleInstance.WriteLog(this, $"Error connecting to Laser: {result.ErrorMessage}", LoglevelEnum.Warning);
                            DoStateChange(TriggerId.ConnectFailed.ToString());
                        }
                        else
                        {
                            LogManager.SingleInstance.WriteLog(this, "Laser connected successfully", LoglevelEnum.Information);
                            DoStateChange(TriggerId.ConnectSuccess.ToString());
                        }
                    }
                    break;
                case EventId.ConnectSuccess:
                    // Sole trigger point for Initialize (Connected -> Initializing).
                    DoStateChange(TriggerId.Initialize.ToString());
                    break;
                case EventId.ConnectFailed:
                    AlarmManager.SingleInstance.SetAlarm(AlarmId.ERRLSR001);
                    break;
                case EventId.Disconnected:
                    {
                        SafeDisconnect();
                        // Clear axis/controller-related state on disconnect so we don't
                        // leave stale references or pending list entries behind.
                        if(_shutdownRequested == 0)
                        {
                            AlarmManager.SingleInstance.SetAlarm(AlarmId.ERRLSR002);
                        }
                    }
                    break;
                case EventId.Initializing:
                    {
                        UpdateInitState(this, InitStateEnum.InitStarted);

                        // Perform the actual initialization work on entering Initializing.
                        LaserCommandResult<bool> result =
                     _laserAdapter.ExecuteCommandAndWait<bool>(LaserWriteCommand.Initialize);
                        if (result.IsSuccess)
                        {
                            DoStateChange(TriggerId.InitSuccess.ToString());
                        }
                        else
                        {
                            DoStateChange(TriggerId.InitFailed.ToString());
                        }
                    }
                    break;
                case EventId.InitSuccess:
                    UpdateInitState(this, InitStateEnum.InitSuccess);
                    this.ResetAllAlarms();
                    break;
                case EventId.InitFailed:
                    UpdateInitState(this, InitStateEnum.InitFailed);
                    break;

                default:
                    break;
            }
            base.OnPostStateChange(eventMsg, stateChangeInfo);
        }

        #endregion

        #region Adapter Selection

        /// <summary>
        /// Instantiates the concrete <see cref="ILaserAdapter"/> for the
        /// selected <paramref name="laserType"/>. Intentionally private
        /// so adapters cannot be swapped mid-lifecycle.
        /// </summary>
        /// <param name="laserType">Configured laser hardware type.</param>
        /// <exception cref="NotSupportedException">
        /// Thrown when <paramref name="laserType"/> is unknown.
        /// </exception>
        private void InitializeAdapter(LaserType laserType)
        {
            switch (laserType)
            {
                case LaserType.TunableToptica:
                    _laserAdapter = LaserAdapterFactory.CreateToptica();
                    break;

                case LaserType.FixedLabView:
                    _laserAdapter = LaserAdapterFactory.CreateFixedLabview();
                    break;

                default:
                    throw new NotSupportedException(
                        $"Unsupported laser type: {laserType}");
            }
        }

        #endregion


        #region Public Commands

        /// <summary>
        /// Executes a write command synchronously on the currently selected
        /// laser adapter by delegating to its <c>ExecuteWait</c> method.
        /// </summary>
        /// <typeparam name="T">Expected result payload type.</typeparam>
        /// <param name="command">Command to execute.</param>
        /// <param name="parameters">Command-specific parameters.</param>
        /// <returns>The adapter's <see cref="LaserCommandResult{T}"/>.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no adapter has been initialized.
        /// </exception>
        public LaserCommandResult<T> ExecuteCommand<T>(
            LaserWriteCommand command,
            params object[] parameters)
        {
            if (!ValidateInitialization())
            {
                return null;
            }
            return _laserAdapter.ExecuteCommandAndWait<T>(command, parameters);
        }

        /// <summary>
        /// Executes a write command asynchronously on the currently selected
        /// laser adapter by delegating to its <c>ExecuteWait</c> method.
        /// </summary>
        /// <typeparam name="T">Expected result payload type.</typeparam>
        /// <param name="command">Command to execute.</param>
        /// <param name="parameters">Command-specific parameters.</param>
        /// <returns>The adapter's <see cref="LaserCommandResult{T}"/>.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no adapter has been initialized.
        /// </exception>
        public async Task<LaserCommandResult<T>> ExecuteCommandAsync<T>(
            LaserWriteCommand command,
            params object[] parameters)
        {
            if (!ValidateInitialization())
            {
                return null;
            }
            return await _laserAdapter.ExecuteCommandAsync<T>(command, parameters);
        }

        /// <summary>
        /// Executes a read command synchronously on the currently selected
        /// laser adapter by delegating to its <c>ExecuteWait</c> method.
        /// </summary>
        /// <typeparam name="T">Expected result payload type.</typeparam>
        /// <param name="command">Command to execute.</param>
        /// <param name="parameters">Command-specific parameters.</param>
        /// <returns>The adapter's <see cref="LaserCommandResult{T}"/>.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no adapter has been initialized.
        /// </exception>
        public LaserCommandResult<T> ExecuteReadProperty<T>(
            LaserReadProperty property)
        {
            if (!ValidateInitialization())
            {
                return null;
            }
            return _laserAdapter.ExecuteReadProperty<T>(property);
        }

        /// <summary>
        /// Guards public API calls against being invoked before an adapter
        /// has been selected. Logs an error and returns <c>false</c> when
        /// the adapter is not initialized.
        /// </summary>
        /// <returns><c>true</c> when the adapter is initialized; otherwise <c>false</c>.</returns>
        private bool ValidateInitialization()
        {
            if (_laserAdapter == null)
            {
                LogManager.SingleInstance.WriteLog(this,
                    "LaserHandler is not initialized.",
                    LoglevelEnum.Error);
                return false;
            }
            return true;
        }

        #endregion
    }
}