namespace MatchApi.Auth;

/// <summary>
/// Marker interface. Any <see cref="Dispatcher.IOpcodeHandler"/> that also implements
/// this interface will be rejected by <see cref="Dispatcher.OpcodeDispatcher"/> with a
/// 9999 UNAUTHORIZED response when no valid JWT is present in the request.
///
/// Protected opcodes: 2001 SAVE_SQUAD, 2002 GET_MY_SCORE, 3002 GET_MY_RANK.
/// </summary>
public interface IAuthenticatedHandler;
