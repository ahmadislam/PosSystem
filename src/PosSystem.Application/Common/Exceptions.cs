namespace PosSystem.Application.Common;

public class AppException(string message) : Exception(message);
public class NotFoundException(string message) : AppException(message);
public class ConflictException(string message) : AppException(message);
public class BadRequestException(string message) : AppException(message);
public class UnauthorizedAppException(string message) : AppException(message);
public class ForbiddenException(string message) : AppException(message);
