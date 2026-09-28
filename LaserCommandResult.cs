namespace HostaApp.ControlSystem.ExtModules.TunableLaser
{
    /// <summary>
    /// Result envelope returned by every <see cref="ILaserAdapter"/> call.
    /// Carries the typed payload together with success/warning/error flags,
    /// an optional driver-specific status code, the raw controller response
    /// (useful for logging/diagnostics), and a human-readable error message
    /// when the operation did not succeed.
    /// </summary>
    /// <typeparam name="T">
    /// Payload type. Typically <see cref="bool"/> for write commands and
    /// the natural CLR type (e.g. <see cref="double"/>, <see cref="string"/>)
    /// for read properties.
    /// </typeparam>
    public sealed class LaserCommandResult<T>
    {
        /// <summary>
        /// <c>true</c> when the operation completed successfully and
        /// <see cref="Value"/> is valid.
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// <c>true</c> when the operation completed but the controller
        /// reported a non-fatal warning. <see cref="Value"/> may still be
        /// valid; inspect <see cref="ErrorMessage"/> for details.
        /// </summary>
        public bool IsWarning { get; set; }

        /// <summary>
        /// <c>true</c> when the operation failed. <see cref="Value"/>
        /// should be considered undefined; <see cref="ErrorMessage"/>
        /// describes the cause.
        /// </summary>
        public bool IsError { get; set; }

        /// <summary>
        /// Optional driver- or controller-specific status/error code.
        /// <c>null</c> when the driver did not provide one.
        /// </summary>
        public int? Code { get; set; }

        /// <summary>
        /// Raw response string as returned by the controller, kept for
        /// logging and diagnostic purposes.
        /// </summary>
        public string RawResponse { get; set; }

        /// <summary>
        /// Human-readable error/warning description. Populated on failure
        /// (and optionally on warnings); <c>null</c> on plain success.
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Typed result value. Only meaningful when <see cref="IsSuccess"/>
        /// is <c>true</c> (or on warnings where the driver still returns data).
        /// </summary>
        public T Value { get; set; }

        /// <summary>
        /// Creates a successful result carrying <paramref name="value"/>.
        /// </summary>
        /// <param name="value">Typed payload returned by the operation.</param>
        /// <param name="raw">Optional raw controller response for diagnostics.</param>
        /// <returns>A result with <see cref="IsSuccess"/> set to <c>true</c>.</returns>
        public static LaserCommandResult<T> Success(T value, string raw = null)
        {
            return new LaserCommandResult<T>
            {
                IsSuccess = true,
                IsWarning = false,
                IsError = false,
                Value = value,
                RawResponse = raw
            };
        }

        /// <summary>
        /// Creates a failure result with the given error message.
        /// </summary>
        /// <param name="message">Human-readable description of the failure.</param>
        /// <param name="raw">Optional raw controller response for diagnostics.</param>
        /// <returns>A result with <see cref="IsError"/> set to <c>true</c>.</returns>
        public static LaserCommandResult<T> Failure(string message, string raw = null)
        {
            return new LaserCommandResult<T>
            {
                IsSuccess = false,
                IsWarning = false,
                IsError = true,
                ErrorMessage = message,
                RawResponse = raw
            };
        }
    }
}
