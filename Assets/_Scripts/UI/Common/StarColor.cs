using UnityEngine;

public static class StarColor
{
    public static Color star1 = "4CAF50".FromHex();
    public static Color star2 = "2196F3".FromHex();
    public static Color star3 = "9C27B0".FromHex();
    public static Color star4 = "FF9800".FromHex();
    public static Color star5 = "F44336".FromHex();

    public static Color GetStarColor(int star)
    {
        switch (star)
        {
            case 1:
                return star1;
            case 2:
                return star2;
            case 3:
                return star3;
            case 4:
                return star4;
            case 5:
                return star5;
            default:
                return star1;
        }
    }
}