#include "position.h"

void uciLoop(int argc, char** argv);

int main(int argc, char** argv) {
    BB::init();
    Zobrist::init();
    uciLoop(argc, argv);
    return 0;
}
