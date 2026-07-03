using System.Globalization;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using FluentAssertions;
using Scribble.Converters;

namespace Scribble.UnitTests.ConverterTests;

public class BoolToBrushConverterTests
{
    private readonly BoolToBrushConverter _sut = new();
    private readonly Type _targetType = typeof(object);
    private const string OwnershipStatus = "ownershipstatus";
    private const string SentStatus = "sentstatus";

    [AvaloniaFact]
    public void Convert_OwnershipStatusTrue_ReturnsOwnMessageBrush()
    {
        var result = (SolidColorBrush)_sut.Convert(true, _targetType, OwnershipStatus, CultureInfo.InvariantCulture);

        result.Should().BeEquivalentTo(BoolToBrushConverter.OwnMessageBrush);
    }

    [AvaloniaFact]
    public void Convert_OwnershipStatusFalse_ReturnsOtherMessageBrush()
    {
        var result = (SolidColorBrush)_sut.Convert(false, _targetType, OwnershipStatus, CultureInfo.InvariantCulture);

        result.Should().BeEquivalentTo(BoolToBrushConverter.OtherMessageBrush);
    }

    [AvaloniaFact]
    public void Convert_SentStatusTrue_ReturnsSentMessageBrush()
    {
        var result = (SolidColorBrush)_sut.Convert(true, _targetType, SentStatus, CultureInfo.InvariantCulture);

        result.Should().BeEquivalentTo(BoolToBrushConverter.SentMessageBrush);
    }

    [AvaloniaFact]
    public void Convert_SentStatusFalse_ReturnsPendingMessageBrush()
    {
        var result = (SolidColorBrush)_sut.Convert(false, _targetType, SentStatus, CultureInfo.InvariantCulture);

        result.Should().BeEquivalentTo(BoolToBrushConverter.PendingMessageBrush);
    }

    [AvaloniaFact]
    public void Convert_InvalidParameter_ReturnsBindingNotificationWithError()
    {
        var result = _sut.Convert(true, _targetType, "invalid", CultureInfo.InvariantCulture);

        result.Should().BeOfType<BindingNotification>()
            .Which.ErrorType.Should().Be(BindingErrorType.Error);
    }

    [AvaloniaFact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Action act = () => _sut.ConvertBack(null, _targetType, null, CultureInfo.InvariantCulture);

        act.Should().Throw<NotSupportedException>();
    }
}