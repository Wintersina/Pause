// Rebuild only the four idle cells of the approved Space Bile Mite strip.
// Pass the original seven-cell strip as input. The last three cells (two
// tells and the hit flash) are copied pixel for pixel.
#import <AppKit/AppKit.h>
#include <math.h>

static int ArmSide(int x, int y)
{
    if ((y >= 80 && x < 60) || (y >= 96 && x < 78)) return -1;
    if ((y >= 80 && x >= 132) || (y >= 96 && x >= 114)) return 1;
    return 0;
}

int main(int argc, const char *argv[])
{
    @autoreleasepool {
        if (argc != 3) return 2;
        NSString *input = [NSString stringWithUTF8String:argv[1]];
        NSString *destination = [NSString stringWithUTF8String:argv[2]];
        NSBitmapImageRep *source = [[NSBitmapImageRep alloc]
            initWithData:[NSData dataWithContentsOfFile:input]];
        if (!source || source.pixelsWide != 1344 || source.pixelsHigh != 192) return 3;
        NSBitmapImageRep *output = [[NSBitmapImageRep alloc]
            initWithBitmapDataPlanes:NULL pixelsWide:1344 pixelsHigh:192 bitsPerSample:8
            samplesPerPixel:4 hasAlpha:YES isPlanar:NO colorSpaceName:NSDeviceRGBColorSpace
            bytesPerRow:0 bitsPerPixel:0];
        if (!output) return 4;

        const int side = 192;
        const int travel[4] = {0, 3, 5, 3}; // relaxed, raised, peak, returning
        NSColor *clear = [NSColor colorWithDeviceRed:0 green:0 blue:0 alpha:0];
        for (int frame = 0; frame < 7; frame++) {
            for (int y = 0; y < side; y++) {
                for (int x = 0; x < side; x++) {
                    int sx = frame < 4 ? x : frame * side + x;
                    int sy = y;
                    if (frame < 4) {
                        int arm = ArmSide(x, y);
                        if (arm && travel[frame]) {
                            // Shoulder holds still; only the outer claw travels.
                            double vertical = fmin(1.0, fmax(0.0, (double)(y - 80) / 70.0));
                            double horizontal = arm < 0 ? (double)(78 - x) / 24.0
                                                        : (double)(x - 113) / 24.0;
                            double reach = vertical * fmin(1.0, fmax(0.0, horizontal));
                            int shift = (int)lround(travel[frame] * reach);
                            sy = y + shift;
                            sx = x - arm * (int)lround(shift * 0.55);
                            if (sx < 0 || sx >= side || sy >= side || ArmSide(sx, sy) != arm) {
                                [output setColor:clear atX:frame * side + x y:y];
                                continue;
                            }
                        }
                    }
                    NSColor *color = [source colorAtX:sx y:sy] ?: clear;
                    [output setColor:color atX:frame * side + x y:y];
                }
            }
        }
        NSData *png = [output representationUsingType:NSBitmapImageFileTypePNG properties:@{}];
        return png && [png writeToFile:destination atomically:YES] ? 0 : 5;
    }
}
