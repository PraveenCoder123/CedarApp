using ControlSystem;
using System;
using System.Threading.Tasks;
using static HostaApp.ControlSystem.ExtModules.TunableLaser.Enums;

namespace HostaApp.ControlSystem.ExtModules.TunableLaser
{
    /// <summary>
    /// Laser adapter for the fixed-wavelength LabVIEW-controlled laser.
    ///
    /// This adapter routes all laser-related operations through the
    /// <see cref="DigitalIOModule"/>, which owns the LabVIEW interface
    /// used to drive the physical laser (via digital I/O commands such
    /// as Laser_On / Laser_Off).
    ///
    /// Only the operations supported by the fixed laser are implemented.
    /// Wavelength/power tuning and health/temperature queries are not
    /// applicable for this hardware and will return a failure result.
    /// </summary>
    public class FixedLabviewLaserService : ILaserAdapter
    {
        private readonly DigitalIOModule _digitalIO;

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="FixedLabviewLaserService"/> class.
        /// </summary>
        /// <param name="digitalIO">
        /// Digital I/O module used to communicate with and control
        /// the LabVIEW-controlled laser.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="digitalIO"/> is <c>null</c>.
        /// </exception>
        public FixedLabviewLaserService(DigitalIOModule digitalIO)
        {
            _digitalIO = digitalIO
                ?? throw new ArgumentNullException(nameof(digitalIO));
        }

        #region Commands

        /// <summary>
        /// Executes a laser write command asynchronously.
        ///
        /// Connection and initialization commands are treated as no-ops
        /// because the fixed LabVIEW laser is managed through the
        /// <see cref="DigitalIOModule"/> lifecycle.
        /// </summary>
        /// <typeparam name="T">
        /// Type of the result expected by the caller.
        /// </typeparam>
        /// <param name="command">
        /// Laser write command to execute.
        /// </param>
        /// <param name="parameters">
        /// Optional parameters associated with the command.
        /// </param>
        /// <returns>
        /// A <see cref="LaserCommandResult{T}"/> indicating whether the
        /// command was executed successfully.
        /// </returns>
        /// <exception cref="NotSupportedException">
        /// Thrown when the specified command is not supported by the
        /// fixed LabVIEW laser.
        /// </exception>
        public Task<LaserCommandResult<T>> ExecuteCommandAsync<T>(
            LaserWriteCommand command,
            params object[] parameters)
        {
            try
            {
                switch (command)
                {
                    case LaserWriteCommand.Connect:
                    case LaserWriteCommand.Disconnect:
                    case LaserWriteCommand.Initialize:

                        /*
                         * The fixed LabVIEW laser is initialized and
                         * connected as part of the DigitalIOModule
                         * lifecycle, so these commands are effectively
                         * no-ops from the adapter's perspective.
                         */

                        return Task.FromResult(
                            LaserCommandResult<T>.Success(
                                ConvertToT<T>(true)));

                    case LaserWriteCommand.TurnOn:
                   // case LaserWriteCommand.Enable:

                        _digitalIO.LaserOn(true);

                        return Task.FromResult(
                            LaserCommandResult<T>.Success(
                                ConvertToT<T>(true)));

                    case LaserWriteCommand.TurnOff:

                        _digitalIO.LaserOn(false);

                        return Task.FromResult(
                            LaserCommandResult<T>.Success(
                                ConvertToT<T>(true)));

                    case LaserWriteCommand.SetWavelength:
                    case LaserWriteCommand.SetPower:
                    case LaserWriteCommand.SetPowerStabilization:

                        return Task.FromResult(
                            LaserCommandResult<T>.Failure(
                                $"Command '{command}' is not supported " +
                                "by the fixed LabVIEW laser."));

                    default:

                        throw new NotSupportedException(
                            $"Unsupported write command: {command}");
                }
            }
            catch (Exception ex)
            {
                LogManager.SingleInstance.WriteLog(
                    this,
                    $"Failed to execute laser command '{command}': {ex.Message}",
                    LoglevelEnum.Error);

                return Task.FromResult(
                    LaserCommandResult<T>.Failure(ex.Message));
            }
        }

        /// <summary>
        /// Executes a laser write command synchronously and waits for
        /// the operation to complete.
        /// </summary>
        /// <typeparam name="T">
        /// Type of the result expected by the caller.
        /// </typeparam>
        /// <param name="command">
        /// Laser write command to execute.
        /// </param>
        /// <param name="parameters">
        /// Optional parameters associated with the command.
        /// </param>
        /// <returns>
        /// A <see cref="LaserCommandResult{T}"/> containing the result
        /// of the command execution.
        /// </returns>
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
        /// Reads a laser property from the fixed LabVIEW laser.
        /// </summary>
        /// <typeparam name="T">
        /// Type of the property value expected by the caller.
        /// </typeparam>
        /// <param name="property">
        /// Laser property to read.
        /// </param>
        /// <returns>
        /// A <see cref="LaserCommandResult{T}"/> containing the requested
        /// property value when supported, or a failure result when the
        /// property is not supported or an error occurs.
        /// </returns>
        /// <exception cref="NotSupportedException">
        /// Thrown when the requested property is not supported by the
        /// fixed LabVIEW laser.
        /// </exception>
        public LaserCommandResult<T> ExecuteReadProperty<T>(
            LaserReadProperty property)
        {
            try
            {
                object value;

                switch (property)
                {
                    case LaserReadProperty.Status:

                        value = _digitalIO.GetLaserStatus(out _);

                        break;

                    case LaserReadProperty.Connected:

                        value = true;

                        break;

                    case LaserReadProperty.WavelengthActual:

                        value = _digitalIO.GetLaserWavelength();

                        break;

                    default:

                        throw new NotSupportedException(
                            $"Unsupported read command: {property}");
                }

                return LaserCommandResult<T>.Success(
                    (T)Convert.ChangeType(
                        value,
                        typeof(T)));
            }
            catch (Exception ex)
            {
                return LaserCommandResult<T>.Failure(ex.Message);
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Converts the specified value to the requested type.
        /// </summary>
        /// <typeparam name="T">
        /// Target type to which the value should be converted.
        /// </typeparam>
        /// <param name="value">
        /// Value to convert.
        /// </param>
        /// <returns>
        /// The converted value, or the default value of
        /// <typeparamref name="T"/> when <paramref name="value"/> is null.
        /// </returns>
        private static T ConvertToT<T>(object value)
        {
            if (value == null)
            {
                return default(T);
            }

            if (value is T variable)
            {
                return variable;
            }

            return (T)Convert.ChangeType(
                value,
                typeof(T));
        }

        #endregion
    }
}