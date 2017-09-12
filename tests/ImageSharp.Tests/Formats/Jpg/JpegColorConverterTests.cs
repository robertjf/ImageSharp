using System;
using System.Numerics;

using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using SixLabors.ImageSharp.Formats.Jpeg.Common.Decoder;
using SixLabors.ImageSharp.Memory;

using Xunit;
using Xunit.Abstractions;
// ReSharper disable InconsistentNaming

namespace SixLabors.ImageSharp.Tests.Formats.Jpg
{
    public class JpegColorConverterTests
    {
        private const float Precision = 1/255f;

        public static readonly TheoryData<int, int, int> CommonConversionData =
            new TheoryData<int, int, int>
                {
                    { 40, 40, 1 },
                    { 42, 40, 2 },
                    { 42, 39, 3 }
                };

        private static readonly ColorSpaceConverter ColorSpaceConverter = new ColorSpaceConverter();

        public JpegColorConverterTests(ITestOutputHelper output)
        {
            this.Output = output;
        }

        private ITestOutputHelper Output { get; }

        private static int R(float f) => (int)MathF.Round(f, MidpointRounding.AwayFromZero);

        // TODO: Move this to a proper test class!
        [Theory]
        [InlineData(0.32, 54.5, -3.5, -4.1)]
        [InlineData(5.3, 536.4, 4.5, 8.1)]
        public void Vector4_PseudoRound(float x, float y, float z, float w)
        {
            var v = new Vector4(x, y, z, w);

            Vector4 actual = v.PseudoRound();

            Assert.Equal(
                R(v.X),
                (int)actual.X
                );
            Assert.Equal(
                R(v.Y),
                (int)actual.Y
            );
            Assert.Equal(
                R(v.Z),
                (int)actual.Z
            );
            Assert.Equal(
                R(v.W),
                (int)actual.W
            );
        }

        [Theory]
        [MemberData(nameof(CommonConversionData))]
        public void ConvertFromYCbCr(int inputBufferLength, int resultBufferLength, int seed)
        {
            ValidateConversion(JpegColorSpace.YCbCr, 3, inputBufferLength, resultBufferLength, seed, ValidateYCbCr);
        }

        private static void ValidateYCbCr(JpegColorConverter.ComponentValues values, Span<Vector4> result, int i)
        {
            float y = values.Component0[i];
            float cb = values.Component1[i];
            float cr = values.Component2[i];
            var ycbcr = new YCbCr(y, cb, cr);

            Vector4 rgba = result[i];
            var actual = new Rgb(rgba.X, rgba.Y, rgba.Z);
            var expected = ColorSpaceConverter.ToRgb(ycbcr);

            Assert.True(actual.AlmostEquals(expected, Precision));
            Assert.Equal(1, rgba.W);
        }

        [Fact]
        public void ConvertFromYCbCr_SimdWithAlignedValues()
        {
            ValidateConversion(JpegColorConverter.FromYCbCrSimd256.ConvertAligned, 3, 64, 64, 1, ValidateYCbCr);
        }

        [Theory]
        [MemberData(nameof(CommonConversionData))]
        public void ConvertFromCmyk(int inputBufferLength, int resultBufferLength, int seed)
        {
            var v = new Vector4(0, 0, 0, 1F);
            var scale = new Vector4(1 / 255F, 1 / 255F, 1 / 255F, 1F);

            ValidateConversion(
                JpegColorSpace.Cmyk,
                4,
                inputBufferLength,
                resultBufferLength,
                seed,
                (values, result, i) =>
                    {
                        float c = values.Component0[i];
                        float m = values.Component1[i];
                        float y = values.Component2[i];
                        float k = values.Component3[i] / 255F;

                        v.X = c * k;
                        v.Y = m * k;
                        v.Z = y * k;
                        v.W = 1F;

                        v *= scale;

                        Vector4 rgba = result[i];
                        var actual = new Rgb(rgba.X, rgba.Y, rgba.Z);
                        var expected = new Rgb(v.X, v.Y, v.Z);

                        Assert.True(actual.AlmostEquals(expected, Precision));
                        Assert.Equal(1, rgba.W);
                    });
        }

        [Theory]
        [MemberData(nameof(CommonConversionData))]
        public void ConvertFromGrayScale(int inputBufferLength, int resultBufferLength, int seed)
        {
            ValidateConversion(
                JpegColorSpace.GrayScale,
                1,
                inputBufferLength,
                resultBufferLength,
                seed,
                (values, result, i) =>
                    {
                        float y = values.Component0[i];
                        Vector4 rgba = result[i];
                        var actual = new Rgb(rgba.X, rgba.Y, rgba.Z);
                        var expected = new Rgb(y / 255F, y / 255F, y / 255F);

                        Assert.True(actual.AlmostEquals(expected, Precision));
                        Assert.Equal(1, rgba.W);
                    });
        }

        [Theory]
        [MemberData(nameof(CommonConversionData))]
        public void ConvertFromRgb(int inputBufferLength, int resultBufferLength, int seed)
        {
            ValidateConversion(
                JpegColorSpace.RGB,
                3,
                inputBufferLength,
                resultBufferLength,
                seed,
                (values, result, i) =>
                    {
                        float r = values.Component0[i];
                        float g = values.Component1[i];
                        float b = values.Component2[i];
                        Vector4 rgba = result[i];
                        var actual = new Rgb(rgba.X, rgba.Y, rgba.Z);
                        var expected = new Rgb(r / 255F, g / 255F, b / 255F);

                        Assert.True(actual.AlmostEquals(expected, Precision));
                        Assert.Equal(1, rgba.W);
                    });
        }

        [Theory]
        [MemberData(nameof(CommonConversionData))]
        public void ConvertFromYcck(int inputBufferLength, int resultBufferLength, int seed)
        {
            var v = new Vector4(0, 0, 0, 1F);
            var scale = new Vector4(1 / 255F, 1 / 255F, 1 / 255F, 1F);

            ValidateConversion(
                JpegColorSpace.Ycck,
                4,
                inputBufferLength,
                resultBufferLength,
                seed,
                (values, result, i) =>
                    {
                        float y = values.Component0[i];
                        float cb = values.Component1[i] - 128F;
                        float cr = values.Component2[i] - 128F;
                        float k = values.Component3[i] / 255F;

                        v.X = (255F - MathF.Round(y + (1.402F * cr), MidpointRounding.AwayFromZero)) * k;
                        v.Y = (255F - MathF.Round(
                                   y - (0.344136F * cb) - (0.714136F * cr),
                                   MidpointRounding.AwayFromZero)) * k;
                        v.Z = (255F - MathF.Round(y + (1.772F * cb), MidpointRounding.AwayFromZero)) * k;
                        v.W = 1F;

                        v *= scale;

                        Vector4 rgba = result[i];
                        var actual = new Rgb(rgba.X, rgba.Y, rgba.Z);
                        var expected = new Rgb(v.X, v.Y, v.Z);

                        Assert.True(actual.AlmostEquals(expected, Precision));
                        Assert.Equal(1, rgba.W);
                    });
        }

        private static JpegColorConverter.ComponentValues CreateRandomValues(
            int componentCount,
            int inputBufferLength,
            int seed,
            float maxVal = 255f)
        {
            var rnd = new Random(seed);
            Buffer2D<float>[] buffers = new Buffer2D<float>[componentCount];
            for (int i = 0; i < componentCount; i++)
            {
                float[] values = new float[inputBufferLength];

                for (int j = 0; j < inputBufferLength; j++)
                {
                    values[j] = (float)rnd.NextDouble() * maxVal;
                }

                // no need to dispose when buffer is not array owner
                buffers[i] = new Buffer2D<float>(values, values.Length, 1);
            }
            return new JpegColorConverter.ComponentValues(buffers, 0);
        }

        private static void ValidateConversion(
            JpegColorSpace colorSpace,
            int componentCount,
            int inputBufferLength,
            int resultBufferLength,
            int seed,
            Action<JpegColorConverter.ComponentValues, Span<Vector4>, int> validatePixelValue)
        {
            ValidateConversion(
                (v, r) => JpegColorConverter.GetConverter(colorSpace).ConvertToRGBA(v, r),
                componentCount,
                inputBufferLength,
                resultBufferLength,
                seed,
                validatePixelValue);
        }

        private static void ValidateConversion(
            Action<JpegColorConverter.ComponentValues, Span<Vector4>> doConvert,
            int componentCount,
            int inputBufferLength,
            int resultBufferLength,
            int seed,
            Action<JpegColorConverter.ComponentValues, Span<Vector4>, int> validatePixelValue)
        {
            JpegColorConverter.ComponentValues values = CreateRandomValues(componentCount, inputBufferLength, seed);
            Vector4[] result = new Vector4[resultBufferLength];

            doConvert(values, result);

            for (int i = 0; i < resultBufferLength; i++)
            {
                validatePixelValue(values, result, i);
            }
        }
    }
}