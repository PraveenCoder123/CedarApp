using System.Threading.Tasks;
using static HostaApp.ControlSystem.ExtModules.TunableLaser.Enums;

namespace HostaApp.ControlSystem.ExtModules.TunableLaser
{
    /// <summary>
    /// Defines the contract for a tunable laser adapter, exposing operations
    /// to execute write commands and read properties on the underlying laser device.
    /// </summary>
    public interface ILaserAdapter
    {
        /// <summary>
        /// Asynchronously executes a write command on the laser.
        /// </summary>
        /// <typeparam name="T">The expected type of the command result value.</typeparam>
        /// <param name="command">The write command to execute.</param>
        /// <param name="parameters">Optional parameters required by the command.</param>
        /// <returns>
        /// A task that resolves to a <see cref="LaserCommandResult{T}"/>
        /// containing the outcome of the command.
        /// </returns>
        Task<LaserCommandResult<T>> ExecuteCommandAsync<T>(
            LaserWriteCommand command,
            params object[] parameters);

        /// <summary>
        /// Synchronously executes a write command on the laser and blocks until it completes.
        /// </summary>
        /// <typeparam name="T">The expected type of the command result value.</typeparam>
        /// <param name="command">The write command to execute.</param>
        /// <param name="parameters">Optional parameters required by the command.</param>
        /// <returns>
        /// A <see cref="LaserCommandResult{T}"/> containing the outcome of the command.
        /// </returns>
        LaserCommandResult<T> ExecuteCommandAndWait<T>(
           LaserWriteCommand command,
           params object[] parameters);

        /// <summary>
        /// Reads the value of the specified property from the laser.
        /// </summary>
        /// <typeparam name="T">The expected type of the property value.</typeparam>
        /// <param name="property">The property to read.</param>
        /// <returns>
        /// A <see cref="LaserCommandResult{T}"/> containing the property value and status.
        /// </returns>
        LaserCommandResult<T> ExecuteReadProperty<T>(
            LaserReadProperty property);
    }
}
