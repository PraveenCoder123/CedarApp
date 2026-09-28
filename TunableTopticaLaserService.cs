using ControlSystem;
using System;
using System.Threading.Tasks;
using TunableLaserDriver;
using static HostaApp.ControlSystem.ExtModules.TunableLaser.Enums;

namespace HostaApp.ControlSystem.ExtModules.TunableLaser
{
    /// <summary>
    /// <see cref="ILaserAdapter"/> implementation for the Toptica CTL
    /// tunable laser. Wraps an <see cref="ITunableLaserDriver"/> and
    /// exposes laser lifecycle, write commands, and readable properties
    /// through the adapter contract.
    /// </summary>
    public class TunableTopticaLaserService : ILaserAdapter
    {
        private readonly ITunableLaserDriver _driver;

        /// <summary>
        /// Creates a new Toptica adapter around the supplied driver.
        /// The driver is injected so the adapter has no dependency on
        /// <see cref="AppSettingsManager"/> or other singletons and can
        /// be unit-tested in isolation.
        /// </summary>
        /// <param name="driver">Toptica driver instance to wrap.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="driver"/> is <c>null</c>.
        /// </exception>
        public TunableTopticaLaserService(ITunableLaserDriver driver)
        {
            _driver = driver
                ?? throw new ArgumentNullException(nameof(driver));
        }

        #region Lifecycle

        /// <summary>
        /// Opens the TCP connection to the Toptica controller and logs
        /// any driver-reported error.
        /// </summary>
        /// <returns>
        /// <c>true</c> if the driver reports a successful connect;
        /// <c>false</c> if the driver returned a failure or threw.
        /// </returns>
        private Task<bool> ConnectAsync()
        {
            try
            {
                var connectResult = _driver.Connect();

                if (!connectResult.Value)
                {
                    LogManager.SingleInstance.WriteLog(this,
                        $"Toptica driver connect failed: {connectResult.ErrorMessage}",
                        LoglevelEnum.Error);
                    return Task.FromResult(false);
                }

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                LogManager.SingleInstance.WriteLog(this,
                    $"Toptica driver connect threw: {ex.Message}",
                    LoglevelEnum.Error);
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Verifies controller communication, reads laser identity/limits,
        /// checks health, ready and temperature-ready flags, and finally
        /// forces emission OFF as a safety measure.
        /// </summary>
        /// <returns>
        /// <c>true</c> when the laser passed every check and emission
        /// was left disabled; <c>false</c> otherwise.
        /// </returns>
        private async Task<bool> InitializeAsync()
        {
            try
            {
                if (_driver == null || !_driver.IsConnected)
                {
                    LogManager.SingleInstance.WriteLog(this,
                        "Laser is not connected.", LoglevelEnum.Warning);
                    return false;
                }

                var systemInfo =await _driver.Device.GetCtlModelAsync();//.CtlModel;
                LogManager.SingleInstance.WriteLog(this,
                    $"Toptica laser system info: {systemInfo.Value}",
                    LoglevelEnum.Information);

                var health =await _driver.Device.GetDeviceHealthAsync();
                if (health.Value != 0)
                {
                    LogManager.SingleInstance.WriteLog(this,
                        $"Laser health check failed. Health = {health}",
                        LoglevelEnum.Warning);
                    return false;
                }

                /*
                 * DeviceBusy == true means the laser is busy and NOT
                 * ready for initialization.
                 */
                var busy =await _driver.Device.GetDeviceBusyAsync();
                if (busy.Value)
                {
                    LogManager.SingleInstance.WriteLog(this,
                        "Laser is busy - not ready for initialization.",
                        LoglevelEnum.Warning);
                    return false;
                }

                var tempReady =await _driver.Temperature.GetTemperatureReadyAsync() ;
                if (!tempReady.Value)
                {
                    LogManager.SingleInstance.WriteLog(this,
                        "Temperature control is not ready.",
                        LoglevelEnum.Warning);
                    return false;
                }

                var minWavelength = _driver.TunableLaser.WavelengthMinimum;
                var maxWavelength = _driver.TunableLaser.WavelengthMaximum;
                LogManager.SingleInstance.WriteLog(this,
                    $"Toptica laser wavelength range: {minWavelength} - {maxWavelength} nm",
                    LoglevelEnum.Information);

                var wavelength = _driver.TunableLaser.WavelengthActual;
                var power = _driver.Power.PowerSetpoint;
                var isEnabled = _driver.Laser.Enabled;

                LogManager.SingleInstance.WriteLog(this,
                    $"Toptica laser state - Wavelength: {wavelength} nm, " +
                    $"Power: {power}, Enabled: {isEnabled}",
                    LoglevelEnum.Information);

                // Ensure emission is OFF after initialization for safety.
                if (isEnabled)
                {
                    await _driver.Laser.SetEnabledAsync(false);
                }

                return true;
            }
            catch (Exception ex)
            {
                LogManager.SingleInstance.WriteLog(this,
                    $"Toptica initialize threw: {ex.Message}",
                    LoglevelEnum.Error);
                return false;
            }
        }

        /// <summary>
        /// Safely disables emission, then closes the controller connection.
        /// Failures to disable emission are logged but do not prevent the
        /// disconnect from proceeding.
        /// </summary>
        /// <returns>
        /// <c>true</c> if the driver disconnect completed without throwing;
        /// <c>false</c> otherwise.
        /// </returns>
        private async Task<bool> DisconnectAsync()
        {
            try
            {
                if (_driver == null || !_driver.IsConnected)
                {
                    LogManager.SingleInstance.WriteLog(this,
                        "Laser is not connected.", LoglevelEnum.Warning);
                    return false;
                }

                try
                {
                    await _driver.Laser.SetEnabledAsync(false);
                }
                catch (Exception ex)
                {
                    LogManager.SingleInstance.WriteLog(this,
                        $"Failed to disable Toptica laser before disconnect: {ex.Message}",
                        LoglevelEnum.Warning);
                }

                _driver.Disconnect();
                return true;
            }
            catch (Exception ex)
            {
                LogManager.SingleInstance.WriteLog(this,
                    $"Toptica disconnect threw: {ex.Message}",
                    LoglevelEnum.Error);
                return false;
            }
        }

        #endregion

        #region Commands

        /// <summary>
        /// Executes a laser write command against the driver and returns
        /// a normalized <see cref="LaserCommandResult{T}"/>. Any exception
        /// thrown by the driver is captured and returned as a failure.
        /// </summary>
        /// <typeparam name="T">Expected payload type of the result.</typeparam>
        /// <param name="command">Write command to execute.</param>
        /// <param name="parameters">
        /// Command-specific parameters. Currently used by
        /// <see cref="LaserWriteCommand.SetWavelength"/> (nm, <see cref="double"/>)
        /// and <see cref="LaserWriteCommand.SetPower"/> (power, <see cref="double"/>).
        /// </param>
        /// <returns>Success or failure result carrying the driver outcome.</returns>
        public async Task<LaserCommandResult<T>> ExecuteCommandAsync<T>(
            LaserWriteCommand command,
            params object[] parameters)
        {
            try
            {
                switch (command)
                {
                    case LaserWriteCommand.Initialize:
                        return SuccessOrFailure<T>(await InitializeAsync(), null);

                    case LaserWriteCommand.Connect:
                        return SuccessOrFailure<T>(await ConnectAsync(), null);

                    case LaserWriteCommand.Disconnect:
                        return SuccessOrFailure<T>(await DisconnectAsync(), null);

                    case LaserWriteCommand.TurnOn:
                        {
                            var r = await _driver.Laser.SetEnabledAsync(true);
                            return SuccessOrFailure<T>(r.Value, r.ErrorMessage);
                        }

                    case LaserWriteCommand.TurnOff:
                        {
                            var r = await _driver.Laser.SetEnabledAsync(false);
                            return SuccessOrFailure<T>(r.Value, r.ErrorMessage);
                        }

                    case LaserWriteCommand.EnableTemperature:
                        {
                            var r = await _driver.Temperature.SetEnabledAsync(true);
                            return SuccessOrFailure<T>(r.Value, r.ErrorMessage);
                        }
                    case LaserWriteCommand.DisableTemperature:
                        {
                            var r = await _driver.Temperature.SetEnabledAsync(false);
                            return SuccessOrFailure<T>(r.Value, r.ErrorMessage);
                        }

                    case LaserWriteCommand.EnablePower:
                        {
                            var r = await _driver.Power.SetEnabledAsync(true);
                            return SuccessOrFailure<T>(r.Value, r.ErrorMessage);
                        }
                    case LaserWriteCommand.DisablePower:
                        {
                            var r = await _driver.Power.SetEnabledAsync(false);
                            return SuccessOrFailure<T>(r.Value, r.ErrorMessage);
                        }

                    case LaserWriteCommand.EnablePiezo:
                        {
                            var r = await _driver.Piezo.SetEnabledAsync(true);
                            return SuccessOrFailure<T>(r.Value, r.ErrorMessage);
                        }
                    case LaserWriteCommand.DisablePiezo:
                        {
                            var r = await _driver.Piezo.SetEnabledAsync(false);
                            return SuccessOrFailure<T>(r.Value, r.ErrorMessage);
                        }

                    case LaserWriteCommand.EnableFeedforward:
                        {
                            var r = await _driver.Power.SetFeedforwardEnabledAsync(true);
                            return SuccessOrFailure<T>(r.Value, r.ErrorMessage);
                        }
                    case LaserWriteCommand.DisableFeedforward:
                        {
                            var r = await _driver.Power.SetFeedforwardEnabledAsync(false);
                            return SuccessOrFailure<T>(r.Value, r.ErrorMessage);
                        }

                    case LaserWriteCommand.SetWavelength:
                        {
                            ValidateParameters(parameters, 1);
                            var r = await _driver.TunableLaser
                                .SetWavelengthSetpointAsync(
                                    Convert.ToDouble(parameters[0]));
                            return SuccessOrFailure<T>(r.Value, r.ErrorMessage);
                        }

                    case LaserWriteCommand.SetPower:
                        {
                            ValidateParameters(parameters, 1);
                            var r = await _driver.Power
                                .SetPowerSetpointAsync(
                                    Convert.ToDouble(parameters[0]));
                            return SuccessOrFailure<T>(r.Value, r.ErrorMessage);
                        }
                    case LaserWriteCommand.SetCurrent:
                        {
                            ValidateParameters(parameters, 1);
                            var r = await _driver.Laser
                                .SetLaserCurrentSetpointAsync(
                                    Convert.ToDouble(parameters[0]));
                            return SuccessOrFailure<T>(r.Value, r.ErrorMessage);
                        }

                    case LaserWriteCommand.SetFeedforward:
                        {
                            ValidateParameters(parameters, 1);
                            var r = await _driver.Power
                                .SetFeedforwardFactorAsync(
                                    Convert.ToDouble(parameters[0]));
                            return SuccessOrFailure<T>(r.Value, r.ErrorMessage);
                        }
                    case LaserWriteCommand.SetTemperature:
                        {
                            ValidateParameters(parameters, 1);
                            var r = await _driver.Temperature
                                .SetTemperatureSetpointAsync(
                                    Convert.ToDouble(parameters[0]));
                            return SuccessOrFailure<T>(r.Value, r.ErrorMessage);
                        }
                    case LaserWriteCommand.SetPiezo:
                        {
                            ValidateParameters(parameters, 1);
                            var r = await _driver.Piezo
                                .SetPiezoVoltageSetpointAsync(
                                    Convert.ToDouble(parameters[0]));
                            return SuccessOrFailure<T>(r.Value, r.ErrorMessage);
                        }

                    default:
                        return LaserCommandResult<T>.Failure(
                            $"Unsupported write command: {command}");
                }
            }
            catch (Exception ex)
            {
                return LaserCommandResult<T>.Failure(ex.Message);
            }
        }

        /// <summary>
        /// Synchronous convenience wrapper over
        /// <see cref="ExecuteCommandAsync{T}(LaserWriteCommand, object[])"/>.
        /// </summary>
        /// <typeparam name="T">Expected payload type of the result.</typeparam>
        /// <param name="command">Write command to execute.</param>
        /// <param name="parameters">Command-specific parameters.</param>
        /// <returns>The awaited command result.</returns>
        /// <remarks>
        /// Do not call from a UI or synchronization-context thread — the
        /// underlying <c>GetAwaiter().GetResult()</c> may deadlock.
        /// </remarks>
        public LaserCommandResult<T> ExecuteCommandAndWait<T>(
            LaserWriteCommand command,
            params object[] parameters)
        {
            try
            {
                return ExecuteCommandAsync<T>(command, parameters)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                return LaserCommandResult<T>.Failure(ex.Message);
            }
        }

        #endregion

        #region Read Properties

        /// <summary>
        /// Reads a laser property from the driver, converting the raw
        /// value to <typeparamref name="T"/>. Unsupported properties
        /// and conversion errors are returned as failure results.
        /// </summary>
        /// <typeparam name="T">Expected value type.</typeparam>
        /// <param name="property">Property to read.</param>
        /// <returns>Success carrying the value, or failure with a message.</returns>
        public LaserCommandResult<T> ExecuteReadProperty<T>(
            LaserReadProperty property)
        {
            try
            {
                object value;
                if(!_driver.IsConnected)
                {
                    return LaserCommandResult<T>.Failure("Connection failed. Laser driver is not connected.");
                }
                switch (property)
                {
                    case LaserReadProperty.Connected:
                        value = _driver.IsConnected;
                        break;

                    case LaserReadProperty.Disconnected:
                        value = !_driver.IsConnected;
                        break;

                    case LaserReadProperty.Enabled:
                        value = _driver.Laser.Enabled;
                        break;

                    case LaserReadProperty.LaserEmissionStatus:
                        value = _driver.Laser.LaserEmissionStatus;
                        break;

                    case LaserReadProperty.WavelengthActual:
                        value = _driver.TunableLaser.WavelengthActual;
                        break;

                    case LaserReadProperty.WavelengthSetpoint:
                        value = _driver.TunableLaser.WavelengthSetpoint;
                        break;

                    case LaserReadProperty.WavelengthMinimum:
                        value = _driver.TunableLaser.WavelengthMinimum;
                        break;

                    case LaserReadProperty.WavelengthMaximum:
                        value = _driver.TunableLaser.WavelengthMaximum;
                        break;

                    case LaserReadProperty.PowerSetpoint:
                        value = _driver.Power.PowerSetpoint;
                        break;

                    case LaserReadProperty.PowerStabilizationEnabled:
                        value = _driver.Power.Enabled;
                        break;

                    case LaserReadProperty.PowerState:
                        value = _driver.Power.PowerState;
                        break;

                    case LaserReadProperty.FeedforwardEnabled:
                        value = _driver.Power.FeedforwardEnabled;
                        break;

                    case LaserReadProperty.LaserCurrentActual:
                        value = _driver.Laser.LaserCurrentActual;
                        break;

                    case LaserReadProperty.LaserCurrentSetpoint:
                        value = _driver.Laser.LaserCurrentSetpoint;
                        break;

                    case LaserReadProperty.LaserCurrentLimit:
                        value = _driver.Laser.LaserCurrentLimit;
                        break;

                    case LaserReadProperty.DeviceHealth:
                        value = _driver.Device.DeviceHealth;
                        break;

                    case LaserReadProperty.Status:
                        value = _driver.Device.DeviceBusy;
                        break;

                    case LaserReadProperty.TemperatureReady:
                        value = _driver.Temperature.TemperatureReady;
                        break;

                    case LaserReadProperty.TemperatureActual:
                        value = _driver.Temperature.TemperatureActual;
                        break;

                    case LaserReadProperty.TemperatureSetpoint:
                        value = _driver.Temperature.TemperatureSetpoint;
                        break;

                    case LaserReadProperty.TemperatureFault:
                        value = _driver.Temperature.TemperatureFault;
                        break;

                    case LaserReadProperty.TemperatureEnabled:
                        value = _driver.Temperature.Enabled;
                        break;

                    case LaserReadProperty.ProductName:
                        value = _driver.Device.ProductName;
                        break;
                  
                    case LaserReadProperty.DeviceLabel:
                        value = _driver.Device.DeviceLabel;
                        break;

                    case LaserReadProperty.CtlModel:
                        value = _driver.Device.CtlModel;
                        break;

                    case LaserReadProperty.SerialNumber:
                        value = _driver.Device.SerialNumber;
                        break;

                    case LaserReadProperty.FirmwareVersion:
                        value = _driver.Device.FirmwareVersion;
                        break;

                    case LaserReadProperty.PiezoEnabled:
                        value = _driver.Piezo.Enabled;
                        break;

                        case LaserReadProperty.PiezoVoltageActual:
                        value = _driver.Piezo.PiezoVoltageActual;
                        break;

                    case LaserReadProperty.PiezoVoltageMaximum:
                        value = _driver.Piezo.PiezoVoltageMaximum;
                        break;
                    case LaserReadProperty.PiezoVoltageMinimum:
                        value = _driver.Piezo.PiezoVoltageMinimum;
                        break;



                    default:
                        return LaserCommandResult<T>.Failure(
                            $"Unsupported read command: {property}");
                }

                return LaserCommandResult<T>.Success(
                    (T)Convert.ChangeType(value, typeof(T)));
            }
            catch (Exception ex)
            {
                return LaserCommandResult<T>.Failure(ex.Message);
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Validates that <paramref name="parameters"/> contains at least
        /// <paramref name="expectedCount"/> entries.
        /// </summary>
        /// <param name="parameters">Parameter array to validate.</param>
        /// <param name="expectedCount">Minimum required number of parameters.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when the array is <c>null</c> or too short.
        /// </exception>
        private static void ValidateParameters(
            object[] parameters, int expectedCount)
        {
            if (parameters == null || parameters.Length < expectedCount)
            {
                throw new ArgumentException(
                    "Required command parameters are missing.");
            }
        }

        /// <summary>
        /// Builds a <see cref="LaserCommandResult{T}"/> from a boolean
        /// outcome plus an optional error message. Replaces the previous
        /// <c>dynamic</c>-based helper that crashed when passed a
        /// <see cref="bool"/> value.
        /// </summary>
        /// <typeparam name="T">Result payload type.</typeparam>
        /// <param name="success">Outcome of the underlying driver call.</param>
        /// <param name="errorMessage">
        /// Error text returned by the driver, or <c>null</c> when none
        /// is available.
        /// </param>
        /// <returns>Success carrying <c>true</c>, or a failure result.</returns>
        private static LaserCommandResult<T> SuccessOrFailure<T>(
            bool success, string errorMessage)
        {
            if (!success)
            {
                return LaserCommandResult<T>.Failure(
                    errorMessage ?? "Operation failed.");
            }

            object boxed = true;
            return LaserCommandResult<T>.Success(
                (T)Convert.ChangeType(boxed, typeof(T)));
        }

        #endregion
    }
}