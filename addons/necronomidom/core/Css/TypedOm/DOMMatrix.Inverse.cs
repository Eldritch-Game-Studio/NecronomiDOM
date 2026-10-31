using System;

namespace NecronomiDom.Css.TypedOm
{
    public partial struct DOMMatrix
    {
        public readonly DOMMatrix Inverse()
        {
            var result = this;
            return result.InvertSelf();
        }

        public DOMMatrix InvertSelf()
        {
            // Preserve the exact 2D embedding and avoid a 4x4 solve for affine CSS.
            if (Is2D)
            {
                double determinant = M11 * M22 - M12 * M21;
                if (determinant != 0 && double.IsFinite(determinant))
                {
                    double a = M22 / determinant, b = -M12 / determinant;
                    double c = -M21 / determinant, d = M11 / determinant;
                    double e = (M21 * M42 - M22 * M41) / determinant;
                    double f = (M12 * M41 - M11 * M42) / determinant;
                    if (double.IsFinite(a) && double.IsFinite(b) && double.IsFinite(c)
                        && double.IsFinite(d) && double.IsFinite(e) && double.IsFinite(f))
                    {
                        this = new DOMMatrix { M11 = a, M12 = b, M21 = c, M22 = d, M41 = e, M42 = f };
                        return this;
                    }
                }
            }
            // Gauss-Jordan elimination with partial pivoting. No determinant epsilon:
            // small but nonzero scales are invertible, and retain double precision.
            Span<double> rows = stackalloc double[32];
            rows.Clear();
            rows[0] = M11; rows[1] = M12; rows[2] = M13; rows[3] = M14;
            rows[8] = M21; rows[9] = M22; rows[10] = M23; rows[11] = M24;
            rows[16] = M31; rows[17] = M32; rows[18] = M33; rows[19] = M34;
            rows[24] = M41; rows[25] = M42; rows[26] = M43; rows[27] = M44;
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                    if (!double.IsFinite(rows[row * 8 + column])) return SetNaN();
                rows[row * 8 + row + 4] = 1;
            }
            for (int column = 0; column < 4; column++)
            {
                int pivotRow = column;
                double maximum = Math.Abs(rows[column * 8 + column]);
                for (int row = column + 1; row < 4; row++)
                {
                    double magnitude = Math.Abs(rows[row * 8 + column]);
                    if (magnitude > maximum) { maximum = magnitude; pivotRow = row; }
                }
                if (maximum == 0 || double.IsNaN(maximum)) return SetNaN();
                if (pivotRow != column)
                    for (int entry = 0; entry < 8; entry++)
                    {
                        int a = column * 8 + entry, b = pivotRow * 8 + entry;
                        (rows[a], rows[b]) = (rows[b], rows[a]);
                    }
                int pivotStart = column * 8;
                double pivot = rows[pivotStart + column];
                // Division rather than multiplying by reciprocal avoids overflowing
                // the reciprocal of a subnormal pivot when the quotient is finite.
                for (int entry = column + 1; entry < 8; entry++) rows[pivotStart + entry] /= pivot;
                rows[pivotStart + column] = 1;
                for (int row = 0; row < 4; row++)
                {
                    if (row == column) continue;
                    int start = row * 8;
                    double factor = rows[start + column];
                    if (factor == 0) continue;
                    for (int entry = column + 1; entry < 8; entry++)
                        rows[start + entry] -= factor * rows[pivotStart + entry];
                    rows[start + column] = 0;
                }
            }
            bool was3D = _is3D;
            M11 = rows[4]; M12 = rows[5]; M13 = rows[6]; M14 = rows[7];
            M21 = rows[12]; M22 = rows[13]; M23 = rows[14]; M24 = rows[15];
            M31 = rows[20]; M32 = rows[21]; M33 = rows[22]; M34 = rows[23];
            M41 = rows[28]; M42 = rows[29]; M43 = rows[30]; M44 = rows[31];
            _is3D = was3D;
            return this;
        }

        private DOMMatrix SetNaN()
        {
            M11 = M12 = M13 = M14 = M21 = M22 = M23 = M24 =
                M31 = M32 = M33 = M34 = M41 = M42 = M43 = M44 = double.NaN;
            _is3D = true;
            return this;
        }
    }
}
